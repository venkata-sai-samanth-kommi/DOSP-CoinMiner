#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp


let serverip =  "192.168.0.100"
let port = 9090 |> string

let serverAddress = "akka.tcp://RemoteServer@" + serverip + ":" + port + "/user/RemoteBoss"
let printerAddress = "akka.tcp://RemoteServer@" + serverip + ":" + port + "/user/RemotePrinter"

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : DEBUG
            loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8778
                    hostname = 192.168.0.100
                }
            }
        }")

let system = ActorSystem.Create("RemoteClient", configuration)

let rem  = system.ActorSelection(serverAddress)
let remoteprinter = system.ActorSelection(printerAddress)

type WorkerMsg =
    | Done of string * string
    | StartWork of string

let sha256: string -> string =
    let byteToHex: byte -> string = fun b -> b.ToString("x2")

    let bytesToHex: byte array -> string =
        fun bytes ->
            bytes
            |> Array.fold (fun a x -> a + (byteToHex x)) ""

    let utf8ToBytes: string -> byte array = System.Text.Encoding.UTF8.GetBytes

    let tosha256: byte array -> byte array =
        fun bytes ->
            use sha256 =
                System.Security.Cryptography.SHA256.Create()
            sha256.ComputeHash(buffer = bytes)

    fun utf8 -> utf8 |> (utf8ToBytes >> tosha256 >> bytesToHex)

let mutable count = 0

let checkZeroes (str: string) (zeroes: int) : Boolean =
    let mutable a = true
    for i = 0 to zeroes - 1 do
        if (str.[i] <> '0') then a <- false
    if a then 
        count <- count+1
    a

let getRandomString = 
    let alphaNumeric = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+={}[]|';:><,./?"
    let charsLen = alphaNumeric.Length
    fun len -> 
        let random = Random()
        let mutable str = ""
        for i in 0..len do 
            let value = random.Next(charsLen)
            let partial = (alphaNumeric.[value]).ToString()
            str <- str+partial            
        str

let worker (mailbox: Actor<_>) = 
    let rec loop () = actor {
        let mutable flag = true
        let! message = mailbox.Receive ()
        let server = mailbox.Sender
        while(flag) do           
            let command = (message|>string).Split ','             
            let len: int = command.[1] |> int
            let k: string = command.[0]
            let value = k |> int             
            let mutable str = getRandomString len
            let encoded = ("vkommi;" + str) |> sha256 
            let shouldConclude : bool = checkZeroes (encoded) (value)
            if shouldConclude then
                flag <- false                        
                mailbox.Context.Sender <! Done ("vkommi;"+str,  encoded)    
        return! loop ()
    }
    loop ()

let mutable i = 0

let mutable strlen = 1

let createWorkers (mailbox: Actor<_>) = 
    let rec loop () = actor {
        let! message = mailbox.Receive ()
        match message with 
            | Done (x,y)  -> 
                        remoteprinter <! (x+" "+y)
                        mailbox.Context.Self.Tell(PoisonPill.Instance, mailbox.Self)
                        mailbox.Context.Stop(mailbox.Context.Self) |> ignore
                        mailbox.Context.Stop(mailbox.Context.Parent) |> ignore                        
                        system.Terminate() |> ignore
            | StartWork x ->  while(i <> 1) do
                                i <- i+1
                                let nameOfTheWorker = "worker" + i.ToString()                                
                                let worker = spawn system nameOfTheWorker worker               
                                worker <! (x+","+strlen.ToString())
                                strlen <- strlen + 1
                        
        return! loop ()
    }
    loop ()

let RemoteClient = 
    spawn system "RemoteClient"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! (message:obj) = mailbox.Receive()
                printfn "%A" message
                let createWorkersRef = spawn system "createWorkers" createWorkers
                createWorkersRef <! StartWork (message.ToString())  
                return! loop()
            }
        printf "Remote Client Started \n" 
        loop()

rem <! "192.168.0.100:8778"
system.WhenTerminated.Wait()