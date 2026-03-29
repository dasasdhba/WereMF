module WereMF.Update.Roll

open FSharpPlus
open FSharpPlus.Data
open WereMF.Common
open WereMF.Module.Cli
open WereMF.Module.Roll
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
    requestInputWithMessage msg parseBool

let rollDraw (r :RollResult) = monad { 
    let! main = State.get
    let rng = main.Rng
    
    let msg = {
        Type = Internal
        Content = "第一晚是否匿名？(1: 是；0: 否)"
    }
    let anonymous = requestInputWithMessage msg parseBool
    
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
        { main with Players = players }
    do! State.put main
    
    let count = main.Players.Length
    let leaf = if count <= minPlayer then false
                elif count >= maxPlayer then true
                else rollAskLeaf ()
    let result = drawWith rng count leaf
    let rolls = [0..(count-1)] |> List.map (fun i ->
        let player = main.Players[i]
        let chara = result[i]
        sendMessage { Type = ToPlayer player ; Content = chara.ToString() }
        {
            Player = player
            Type = chara
            Reset = false
        }
    )
    { r with Rolls = rolls }
}

let rollReset (roll : RollResult) = monad {
    let! main = Reader.ask
    let rng = main.Rng
    
    let rec loop r =
        let remainingBar = getRemainingBar r
        let remainingBoom = getRemainingBoom r
        let resetRolls = getResetRolls (remainingBar.Length > 0) (remainingBoom.Length > 0) r
        
        if resetRolls.Length = 0 then r else

        let msg = {
            Type = Internal
            Content = "输入需要重抽身份的玩家，输入 0 以继续"
        }
        let parser = parseInt >> (function
           | Ok i when i <= 0 -> Ok i
           | Ok i ->
                let pId = PlayerId i
                let p = r.Rolls |> List.tryFind (fun s -> s.Player.Id = pId)
                match p with
                | Some player ->
                    if resetRolls |> List.exists (fun s -> s = player) then Ok i
                    else Error $"玩家 {player.Player.ToCliString()} 无法重抽身份"
                | None -> Error $"玩家 {pId} 不存在"
           | value -> value
        )

        let result = requestInputWithMessage msg parser
        if result <= 0 then r else
        
        let pId = PlayerId result
        let p = r.Rolls |> List.find (fun s -> s.Player.Id = pId)
        let camp = p.Type.GetCamp()
        let pool = if (camp = Bar) then remainingBar
                   else remainingBoom
        let newChara = pool |> List.randomChoiceWith rng
        sendMessage { Type = ToPlayer p.Player ; Content = newChara.ToString() }
        let newP = { p with Type = newChara ; Reset = true }
        let newRolls = r.Rolls |> List.map (fun s ->
            if s = p then newP else s
        )
        loop { r with Rolls = newRolls }
        
    loop roll
}

let rollInputLeaf (leaf : RollPair) =
    let msg = { Type = ToPlayer leaf.Player ; Content = "输入叶子的四个身份" }
    let isInvalidCharas c = c = FenXia || c = CaiMon || c = Leaf
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

let rollSetLeaf (r : RollResult) = monad {
    let! main = Reader.ask
    let rng = main.Rng
    let ye = r.Rolls |> List.tryFind (fun s -> s.Type = Leaf)
    match ye with
    | None -> r
    | Some leaf ->
        let result = rollInputLeaf leaf
        let result = result |> List.randomShuffleWith rng
        sendMessage { Type = ToPlayer leaf.Player ; Content = $"第一身份：{result.Head}" }
        let r = { r with LeafRolls = result }
        
        let msg = { Type = ToPlayer leaf.Player ; Content = "是否重抽第一身份？（1：重抽；0：放弃）" }
        let result = requestInputWithMessage msg parseBool
        if not result then r else
        
        let head = r.LeafRolls.Head
        let remaining = r.LeafRolls.Tail
        let list = (remaining |> List.randomShuffleWith rng) @ [head]
        sendMessage { Type = ToPlayer leaf.Player ; Content = $"第一身份：{list.Head}" }
        { r with LeafRolls = list }
}

let createEntities (r : RollResult) =
    r.Rolls |> List.map (fun p ->
            { Player = p.Player ; Role = p.Type |> createRole r ; State = EntityState.New() }
        )

let rollUpdate () = monad {
    let! main = State.get
    let roll = main.Roll
    let r, main = State.run (rollDraw roll) main
    let r = Reader.run (rollReset r) main
    let r = Reader.run (rollSetLeaf r) main
    let main = { main with Roll = r }
    do! State.put main
    let entities = createEntities r
    GameState.New entities |> Game
}