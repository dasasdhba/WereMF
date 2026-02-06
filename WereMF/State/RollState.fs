module WereMF.State.RollState

open System
open System.IO
open System.Text
open System.Text.Json
open FSharpPlus
open WereMF.Type.Chara
open WereMF.Type.Player

type CharaRoll =
    {
        Type : CharaType
        Prob : float
    }
    
type RollPair =
    {
        Player : Player
        Type : CharaType
        Reset : bool
    }
    
type RollContext = {
    Rolls : RollPair list
    LeafRolls : CharaType list
}

let newRollContext = { Rolls = [] ; LeafRolls = [] }

// -----------------------------------------------------------
// draw

type CharaRollJson = {
    Chara : string
    Prob : float
}

type CharaRollJsonArray = {
    Charas : CharaRollJson array
}

let tryLoadConfig (path: string) =
    if File.Exists(path) then
        try
            let json = File.ReadAllText(path, Encoding.UTF8)
            let result = JsonSerializer.Deserialize<CharaRollJsonArray>(json)
            Ok result
        with
        | ex -> Error $"{path}: JSON 解析失败 {ex.Message}"
    else
        Error $"{path} 不存在"

let jsonRolls = monad {
    let! json = tryLoadConfig "config.json"
    return json.Charas 
           |> Array.fold (fun r jr ->
               let result = CharaType.Create jr.Chara
               match result with
               | Error msg ->
                   Console.WriteLine $"[Init] Config: {msg}"
                   r
               | Ok value -> { Type = value; Prob = jr.Prob } :: r
           ) []
}
    
let barPoolDefault = [
    { Type = JiaoHua ; Prob = 1 }
    { Type = Doge ; Prob = 0.5 }
    { Type = Doctor ; Prob = 0.5 }
    { Type = Mole ; Prob = 0.5 }
    { Type = Rabi ; Prob = 0.5 }
    { Type = SheLang ; Prob = 0.5 }
    { Type = FaMao ; Prob = 0.5 }
    { Type = Kirby ; Prob = 0.5 }
    { Type = FenXia ; Prob = 0.5 }
    { Type = Creeper ; Prob = 0.5 }
]

let boomPoolDefault = [
    { Type = PaoXian ; Prob = 1 }
    { Type = ShiWu ; Prob = 0.5 }
    { Type = HuiKa ; Prob = 0.5 }
    { Type = YinMo ; Prob = 0.5 }
    { Type = CTF ; Prob = 0.5 }
    { Type = HeChong ; Prob = 0.5 }
    { Type = CaiMon ; Prob = 0.5 }
    { Type = XianSong ; Prob = 0.5 }
    { Type = JiangXian ; Prob = 0.5 }
    { Type = Myz ; Prob = 0.5 }
]

let barPool =
    match jsonRolls with
    | Ok pool ->
        let bar = pool |> List.filter (fun x -> x.Type.GetCamp() = Bar && x.Prob > 0)
        if bar.Length < 4 then
            Console.WriteLine "[Init] 吧方角色不足, 使用默认配置"
            barPoolDefault
        else
            bar
    | Error e ->
        Console.WriteLine $"[Init] {e}, 吧方使用默认配置"
        barPoolDefault

let boomPool =
    match jsonRolls with
    | Ok pool ->
        let boom = pool |> List.filter (fun x -> x.Type.GetCamp() = Boom && x.Prob > 0)
        if boom.Length < 3 then
            Console.WriteLine "[Init] 爆方角色不足，使用默认配置"
            boomPoolDefault
        else
            boom
    | Error e ->
        Console.WriteLine $"[Init] {e}, 爆方使用默认配置"
        boomPoolDefault

let getMaxBarAndBoom () =
    let bar = barPool.Length
    let boom = boomPool.Length
    if bar <= boom then
        bar + bar - 1
    else
        boom + boom + 1

let MinPlayer = 7
let MaxPlayer = max 7 (getMaxBarAndBoom() + 1)

// random draw powered by Kimi
let drawFromPoolWith (random : Random) (pool: CharaRoll list) (count: int) : CharaType list =
    let guaranteed = pool |> List.filter (fun x -> x.Prob >= 1.0) |> List.map (fun x -> x.Type)
    let available = pool |> List.filter (fun x -> x.Prob > 0.0 && x.Prob < 1.0)
    
    let rec weightedRandomDraw (n: int) (available: CharaRoll list) (acc: CharaType list) : CharaType list =
        if n <= 0 || available.IsEmpty then
            acc
        else
            let totalWeight = available |> List.sumBy (fun x -> x.Prob)
            let randVal = random.NextDouble() * totalWeight
            
            let rec selectItem (items: CharaRoll list) (cumWeight: float) (prev: CharaRoll list) : CharaRoll * CharaRoll list =
                match items with
                | head :: tail ->
                    let newCumWeight = cumWeight + head.Prob
                    if randVal <= newCumWeight then
                        (head, prev @ tail)
                    else
                        selectItem tail newCumWeight (prev @ [head])
                | [] -> failwith "Should not reach here"
            
            let selected, remaining = selectItem available 0.0 []
            weightedRandomDraw (n - 1) remaining (selected.Type :: acc)
    
    let randomCount = max 0 (count - guaranteed.Length)
    let randomResults = weightedRandomDraw randomCount available []
    
    guaranteed @ randomResults |> List.take (min count (guaranteed.Length + available.Length))

let getBarBoomCount count =
    let barCount = if (count % 2) = 0 then count / 2 + 1 else count / 2
    let boomCount = count - barCount
    barCount, boomCount

let drawWith (random : Random) (count :int) (leaf : bool) : CharaType list =
    let barCount, boomCount = getBarBoomCount (if leaf then count - 1 else count)
    
    // 粉侠仅叶子局，且粉彩不同时出场版本
    //let bars = if leaf then barPool else barPool |> List.filter (fun x -> x.Type <> FenXia)
    //let barCharas = drawFromPool bars barCount
    //let booms = if barCharas |> List.exists (fun x -> x = FenXia) then
    //                boomPool |> List.filter (fun x -> x.Type <> CaiMon)
    //            else
    //                boomPool
    //let boomCharas = drawFromPool booms boomCount
    
    let barCharas = drawFromPoolWith random barPool barCount
    let boomCharas = drawFromPoolWith random boomPool boomCount
    let result = barCharas @ boomCharas
    let result = if leaf then Leaf :: result else result
    result |> List.randomShuffleWith random