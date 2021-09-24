#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#time "on"
open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp


let k = fsi.CommandLineArgs.[1]

let getClientAddress (clientip: string)(port: string) = 
    "akka.tcp://RemoteClient@" + clientip + ":" + port + "/user/RemoteClient"

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
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
                    port = 9090
                    hostname = 192.168.0.100
                }
            }
        }")

let system = ActorSystem.Create("RemoteServer", configuration)

let RemotePrinter = 
    spawn system "RemotePrinter"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! (message:obj) = mailbox.Receive()
                printfn "%s" (message.ToString())

                return! loop()
            }
        loop()

let RemoteBoss = 
    spawn system "RemoteBoss"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! (message:obj) = mailbox.Receive()
                let command = (message|>string).Split ':' 
                let rem  = system.ActorSelection(getClientAddress command.[0] command.[1])
                rem <! (k.ToString()) 
                return! loop()
            }
        printf "Remote Boss Started \n" 
        loop()

system.WhenTerminated.Wait()