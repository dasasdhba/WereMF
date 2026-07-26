module WereMF.Update.Roll

open FSharp.Data
open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Roll
open WereMF.Module.Api
open WereMF.Role.Bind
open WereMF.State
    
let getRemainingBar (r : RollResult) : CharaType list =
    barCharaPool |> List.filter (fun c ->
        r.Rolls |> List.exists (fun s -> s.Type = c) |> not
    )

let getRemainingBoom (r : RollResult) : CharaType list =
    boomCharaPool |> List.filter (fun c ->
        r.Rolls |> List.exists (fun s -> s.Type = c) |> not
    )

let getResetRolls hasBar hasBoom (r : RollResult) : RollPair list =
    r.Rolls |> List.filter (fun r ->
        let camp = r.Type.GetCamp()
        (not r.Reset) && ((camp = Bar && hasBar) || (camp = Boom && hasBoom))
    )
    
let rollAskLeaf () =
    let msg = {
        Type = Internal
        Content = "是否为叶子局？(1: 是；0: 否)"
    }
    requestInputWithRawMessage msg ApiType.RequestLeafGame parseBool

let rollDraw () = monad { 
    let! main = Reader.ask
    let rng = main.Rng
    
    let msg = {
        Type = Internal
        Content = "第一晚是否匿名？(1: 是；0: 否)"
    }
    let anonymous = requestInputWithRawMessage msg ApiType.RequestAnonymousGame parseBool
    
    let main =
        if anonymous |> not then
            let players =
                main.Players |> List.map (fun p -> { p with Anonymous = false })
            { main with Players = players }
        else
        
        let idp =
            main.Players |> List.randomShuffleWith rng |> List.indexed
        let players =
            idp |> List.map (fun (i, p) -> { p with Id = PlayerId (i + 1) ; Anonymous = true })
        sendApi {
            Type = Internal
            Content = ""
            Api = ApiType.PlayerAnonymousInit
            Data = players |> List.mapJson (fun p -> p.ToJsonValue())
        }
        { main with Players = players }
    
    let count = main.Players.Length
    let leaf = if count <= minPlayer then false
                elif count >= maxPlayer then true
                else rollAskLeaf ()
    let result = drawWith rng count leaf
    let rolls = [0..(count-1)] |> List.map (fun i ->
        let player = main.Players[i]
        let chara = result[i]
        sendRawMessage { Type = ToPlayer player ; Content = chara.ToString() } ApiType.PlayerNotifyChara
        {
            PlayerId = player.Id
            Type = chara
            Reset = false
        }
    )
    { main with Roll.Rolls = rolls }
}

let rollReset () = monad {
    let! main = Reader.ask
    let rng = main.Rng
    
    let rec loop r =
        let remainingBar = getRemainingBar r
        let remainingBoom = getRemainingBoom r
        let resetRolls = getResetRolls (remainingBar.Length > 0) (remainingBoom.Length > 0) r
        
        if resetRolls.Length = 0 then r else
        
        let filter = function
           | Ok i when i <= 0 -> Ok i
           | Ok i ->
                let pId = PlayerId i
                let p = r.Rolls |> List.tryFind (fun s -> s.PlayerId = pId)
                match p with
                | Some pair ->
                    if resetRolls |> List.exists (fun s -> s = pair) then Ok i
                    else
                        let player = main.GetPlayer pair.PlayerId
                        Error $"玩家 {player.ToCliString()} 无法重抽身份"
                | None -> Error $"玩家 {pId} 不存在"
           | value -> value
        let msg = {
            Type = Internal
            Content = "输入需要重抽身份的玩家，输入 0 以继续"
            Api = ApiType.RequestRerollPlayer
            Data =
                [1..main.Players.Length]
                |> List.choose (fun i ->
                    match Ok i |> filter with
                    | Ok i -> Some (decimal i |> JsonValue.Number)
                    | Error _ -> None
                    )
                |> List.toArray
                |> JsonValue.Array
        }
        let parser = parseInt >> filter
        let result = requestInputWithMessage msg parser
        if result <= 0 then r else
        
        let pId = PlayerId result
        let p = r.Rolls |> List.find (fun s -> s.PlayerId = pId)
        let camp = p.Type.GetCamp()
        let pool = if (camp = Bar) then remainingBar
                   else remainingBoom
        let newChara = pool |> List.randomChoiceWith rng
        let player = main.GetPlayer p.PlayerId
        sendRawMessage { Type = ToPlayer player ; Content = newChara.ToString() } ApiType.PlayerNotifyCharaReset
        let newP = { p with Type = newChara ; Reset = true }
        let newRolls = r.Rolls |> List.map (fun s ->
            if s = p then newP else s
        )
        loop { r with Rolls = newRolls }
        
    let roll = loop main.Roll
    { main with Roll = roll }
}

let rollInputLeaf (player: Player)=
    let isInvalidCharas c = c = FenXia || c = CaiMon || c = Leaf
    let choices =
        (barCharaPool @ boomCharaPool)
        |> List.distinct
        |> List.filter (isInvalidCharas >> not)
        |> List.map (fun c -> JsonValue.Record [|
            "value", c.ToJsonValue ()
            "camp", c.GetCamp().ToJsonValue ()
        |])
        |> List.toArray
        |> JsonValue.Array
    let msg = {
        Type = ToPlayer player
        Content = "输入叶子的四个身份"
        Api = ApiType.RequestLeafCharas
        Data = JsonValue.Record [|
            "kind", JsonValue.String "role_set"
            "choice_count", JsonValue.Number 4M
            "required_camps", JsonValue.Array [| JsonValue.String "吧方"; JsonValue.String "爆方" |]
            "excluded", JsonValue.Array [| JsonValue.String "粉侠"; JsonValue.String "彩怪"; JsonValue.String "叶子" |]
            "options", choices
        |]
    }
    let parser = parseCharaList >> (function
        | Ok list when list.Length <> 4 ->
            Error "请输入四个不重复的身份"
        | Ok list when list |> List.exists isInvalidCharas ->
            Error $"无效的身份：{list |> List.find isInvalidCharas}"
        | Ok list when (list |> List.filter (fun c -> c.GetCamp() = Bar)).Length = 4
             || (list |> List.filter (fun c -> c.GetCamp() = Boom)).Length = 4 ->
            Error "必须同时包含吧方和爆方身份"
        | value -> value
    )
    requestInputWithMessage msg parser
let rollSetLeaf () = monad {
    let! main = Reader.ask
    let rng = main.Rng
    let r = main.Roll
    let ye = r.Rolls |> List.tryFind (fun s -> s.Type = Leaf)
    let r = 
        match ye with
        | None -> r
        | Some leaf ->
            let player = main.GetPlayer leaf.PlayerId
            let result = rollInputLeaf player
            let result = result |> List.randomShuffleWith rng
            sendMessage {
                Type = ToPlayer player
                Content = $"第一身份：{result.Head}"
                Api = ApiType.LeafNotifyFirstChara
                Data = result.Head.ToJsonValue ()
            }
            let r = { r with LeafRolls = result }
            
            let msg = { Type = ToPlayer player ; Content = "是否重抽第一身份？（1：重抽；0：放弃）" }
            let result = requestInputWithRawMessage msg ApiType.RequestLeafCharaReroll parseBool
            if not result then r else
            
            let head = r.LeafRolls.Head
            let remaining = r.LeafRolls.Tail
            let list = (remaining |> List.randomShuffleWith rng) @ [head]
            sendMessage {
                Type = ToPlayer player
                Content = $"第一身份：{list.Head}"
                Api = ApiType.LeafNotifyFirstCharaReroll
                Data = list.Head.ToJsonValue ()
            }
            { r with LeafRolls = list }
    { main with Roll = r }
}

let createEntities (main : MainContext) =
    main.Roll.Rolls |> List.map (fun p ->
            { Player = main.GetPlayer p.PlayerId ; Role = p.Type |> createRole main.Roll ; State = EntityState.New() }
        )

let rollUpdate () = monad {
    let! main = State.get
    let main = Reader.run (rollDraw ()) main
    let main = Reader.run (rollReset ()) main
    let main = Reader.run (rollSetLeaf ()) main
    do! State.put main
    let entities = createEntities main
    GameState.New entities |> Game
}