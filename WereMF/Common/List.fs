module WereMF.Common.List

open FSharp.Data

let mapJson f (list: List<'T>) =
    list |> List.map f |> List.toArray |> JsonValue.Array