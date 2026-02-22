open System
open System.Text
open WereMF.Module.Roll
open WereMF.Update.Main

type CliOptions = {
    Help: bool
    Api: bool
    Config: string option
}

let args = Environment.GetCommandLineArgs() |> Array.skip 1 |> Array.toList

let rec parseArgs args acc =
    match args with
    | [] -> acc
    | "--help" :: rest -> parseArgs rest { acc with Help = true }
    | "--api" :: rest -> parseArgs rest { acc with Api = true }
    | "--config" :: path :: rest -> parseArgs rest { acc with Config = Some path }
    | "--config" :: [] -> failwith "--config requires a path"
    | unknown :: _ -> failwith $"Unknown argument: {unknown}"

let options = parseArgs args { Help = false; Api = false; Config = None }

if options.Help then
    printfn "Usage: WereMF [--help] [--api] [--config <path>]"
    exit 0

Console.OutputEncoding <- Encoding.UTF8
Console.InputEncoding <- Encoding.UTF8

initRollPools (defaultArg options.Config "config.json")
launchMain() |> ignore