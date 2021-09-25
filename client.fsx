#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            stdout-loglevel : off
            loglevel : off
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""              
            }
            remote {
                helios.tcp {
                    port = 8778
                    hostname = localhost
                }
            }
        }")

let system = ActorSystem.Create("RemoteClient", configuration)

let serverIPAdress =  fsi.CommandLineArgs.[1]
let serverPort = 9090 |> string
let universityOfFloridaUserName = "vkommi;"

let getRemoteAddress (serverip : string) (port : string) (actorName : string) =
    "akka.tcp://RemoteServer@" + serverip + ":" + port + "/user/" + actorName

type WorkState =
    | Finished of string * string
    | Begin of string

let getHash: string -> string =
    let convertToSHA256: byte array -> byte array =
        fun bytesToBeConverted ->
            use sha256 =
                System.Security.Cryptography.SHA256.Create()
            sha256.ComputeHash(buffer = bytesToBeConverted)
    fun str -> str |> (System.Text.Encoding.UTF8.GetBytes >> convertToSHA256 >> fun res -> res |> Array.fold (fun s1 s2 -> s1 + (s2.ToString("x2"))) "")


let checkIfHashBeginsWithKZeros (hash: string) (k: int) : Boolean =

    let mutable count = 0
    let mutable breakLoop = false
    let mutable result = false

    for i = 0 to hash.Length do
        if ( not breakLoop) then
            if (hash.[i] = '0') then
                count <- count+1                
            else                
                breakLoop <- true

    if (count >= k) then
        result <- true

    result

let randomStringGenerator = 
    fun desiredSize -> 
        let randomInstance = Random()
        let mutable randomString = ""
        let alphaNumeric = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-+={}[]|';:><,./?"
        for i in 0..desiredSize do 
            let value = randomInstance.Next(alphaNumeric.Length)
            let partial = (alphaNumeric.[value]).ToString()
            randomString <- randomString+partial 
        randomString

let CoinMiner (postBox: Actor<_>) = 
    let rec loop () = actor {
        let mutable shouldConclude = false
        let! order = postBox.Receive ()
        while(not shouldConclude) do           
            let random = Random()
            let numberOfZeros = (order|> string |> int)                       
            let randomString = randomStringGenerator (random.Next(60) |> int)
            let hashValue = (universityOfFloridaUserName + randomString) |> getHash             
            shouldConclude <- checkIfHashBeginsWithKZeros (hashValue) (numberOfZeros)            
            if shouldConclude then        
                postBox.Context.Sender <! Finished (universityOfFloridaUserName+randomString,  hashValue) 

        return! loop ()
    }
    loop ()

let remoteServer  = system.ActorSelection(getRemoteAddress serverIPAdress serverPort "Lord")
let remoteprinter = system.ActorSelection(getRemoteAddress serverIPAdress serverPort "PrintingActor")

let workersGenerator (postBox: Actor<_>) = 
    let rec loop () = actor {
        let! order = postBox.Receive ()
        match order with 
            | Finished (str, hash)  -> 
                            remoteprinter <! (str+" "+hash)
                            printfn "Results generated and sent to the server!"
                            printf "------------------------------------------------------------------------------------\n" 
                            postBox.Context.Self.Tell(PoisonPill.Instance, postBox.Self)
                            postBox.Context.Stop(postBox.Context.Self) |> ignore
                            postBox.Context.Stop(postBox.Context.Parent) |> ignore                        
                            system.Terminate() |> ignore

            | Begin numberOfZeros ->    printfn "generating workers on client side"
                                        let arrWorkers=[for i in 1..2 do yield(spawn system ("Miner" + i.ToString())) CoinMiner]                                                                  
                                        let workerSystem = system.ActorOf(Props.Empty.WithRouter(Akka.Routing.RoundRobinGroup(arrWorkers)))
                                        workerSystem <! numberOfZeros                                           
                        
        return! loop ()
    }
    loop ()

let Slave = 
    spawn system "Slave"
    <| fun postBox ->
        let rec loop() =
            actor {
                let! (order:obj) = postBox.Receive()
                if ((order |> string) <> "die") then
                    printfn "Received number of leading zero's = %s from the server" (order.ToString())      
                    let createWorkersRef = spawn system "createWorkers" workersGenerator
                    createWorkersRef <! Begin (order.ToString()) 
                else 
                    printfn "Coin found by some other :-( terminating self....."
                    postBox.Context.Self.Tell(PoisonPill.Instance, postBox.Self)                      
                    system.Terminate() |> ignore
                return! loop()
            }
        printf "------------------------------------------------------------------------------------\n" 
        printf "Remote client Started \n" 
        loop()

remoteServer <! "localhost:8778"

system.WhenTerminated.Wait()
