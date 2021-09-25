#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#time "on"
open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp


let numberOFZeros = fsi.CommandLineArgs.[1]
let universityOfFloridaUserName = "vkommi;"

let getClientAddress (clientip: string)(port: string) = 
    "akka.tcp://RemoteClient@" + clientip + ":" + port + "/user/Slave"

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
                    port = 9090
                    hostname = 192.168.0.100
                }
            }
        }")

let system = ActorSystem.Create("RemoteServer", configuration)


let mutable clients: string[] = [||]

let PrintingActor = 
    spawn system "PrintingActor"
    <| fun postBox ->
        let rec loop() =
            actor {
                let! (order:obj) = postBox.Receive()
                let output = order.ToString().Split " "
                printfn "------------------------------------------------------------------------------------"
                printfn "Output string : %s" (output.[0])
                printfn "Output hash   : %s" (output.[1])
                printfn "------------------------------------------------------------------------------------"
                printfn "killing the following clients ::: %A" clients
                for client in clients do 
                            printfn "%s" client
                            let ipAndPort = (client|>string).Split ':' 
                            let slave  = system.ActorSelection(getClientAddress ipAndPort.[0] ipAndPort.[1])
                            slave <! "die"  
                postBox.Context.Self.Tell(PoisonPill.Instance, postBox.Self)
                postBox.Context.Stop(postBox.Context.Self) |> ignore
                postBox.Context.Stop(postBox.Context.Parent) |> ignore                        
                system.Terminate() |> ignore
                return! loop()
            }
        loop()

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

let workersGenerator (postBox: Actor<_>) = 
    let rec loop () = actor {
        let! order = postBox.Receive ()
        match order with 
            | Finished (str, hash)  -> 
                                        PrintingActor <! (str+" "+hash)

            | Begin numberOfZeros ->    printfn "generating workers on server side"
                                        let arrWorkers=[for i in 1..2 do yield(spawn system ("Miner" + i.ToString())) CoinMiner]                                                                  
                                        let workerSystem = system.ActorOf(Props.Empty.WithRouter(Akka.Routing.RoundRobinGroup(arrWorkers)))
                                        workerSystem <! numberOfZeros
                        
        return! loop ()
    }
    loop ()


let Lord = 
    spawn system "Lord"
    <| fun postBox ->
        let rec loop() =
            actor {
                let! (order:obj) = postBox.Receive()
                clients <- Array.append clients [|((order |> string))|]
                printfn "Currently connected clients %A" clients
                let ipAndPort = (order|>string).Split ':' 
                printfn "Connected to %s and sent number of leading zero's = %s" (order.ToString()) numberOFZeros
                let slave  = system.ActorSelection(getClientAddress ipAndPort.[0] ipAndPort.[1])
                slave <! (numberOFZeros.ToString()) 
                return! loop()
            }
        printf "Remote server started and ready to accept connections\n" 
        loop()

let createWorkersRef = spawn system "workersGenerator" workersGenerator
createWorkersRef <! Begin (numberOFZeros.ToString()) 

system.WhenTerminated.Wait()
