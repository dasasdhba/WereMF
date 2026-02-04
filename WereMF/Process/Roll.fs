module WereMF.Roll

open FSharpPlus
open FSharpPlus.Data
open WereMF.Chara
open WereMF.GameState
open WereMF.Player
open WereMF.RollState
open WereMF.Cli
    
let getRemainingBar (r : RollState) : CharaType list =
    barPool |> List.filter (fun c ->
        r.Rolls |> List.exists (fun s -> s.Type = c.Type) |> not
    ) |> List.map (fun c -> c.Type)

let getRemainingBoom (r : RollState) : CharaType list =
    boomPool |> List.filter (fun c ->
        r.Rolls |> List.exists (fun s -> s.Type = c.Type) |> not
    ) |> List.map (fun c -> c.Type)

let getResetRolls (hasBar) (hasBoom) (r : RollState) : RollPair list =
    r.Rolls |> List.filter (fun r ->
        let camp = r.Type.GetCamp()
        (not r.Reset) && ((camp = Bar && hasBar) || (camp = Boom && hasBoom))
    )
    
let rollInit (r: RollState) : State<GameStack, GameStack * bool>= monad {
    let! current = State.get
    let count = current.Players.Length
    let next = if count <= MinPlayer then
                   RollLeaf false |> Draw
               elif count >= MaxPlayer then
                   RollLeaf true |> Draw
               else
                   AskLeaf
    let r = r.SetStatus next
    let current = r |> Roll |> current.SetStatus
    do! State.put current
    return current, false
}

let rollDraw (r :RollState) rollLeaf : State<GameStack, GameStack * bool> = monad { 
    let! current = State.get
    let count = current.Players.Length
    let result = draw count rollLeaf
    let rolls = [0..(count-1)] |> List.map (fun i ->
        let player = current.Players[i]
        let chara = result[i]
        sendMessage { Type = ToPlayer player ; Content = chara.ToString() }
        {
            Player = player
            Type = chara
            Reset = false
        }
    )
    let r = rolls |> r.SetRolls
    let r = Reset |> r.SetStatus
    let current = r |> Roll |> current.SetStatus
    do! State.put current
    return current, false
}

let rollAskLeaf (r : RollState) : State<GameStack, GameStack * bool> = monad {
    let msg = {
        Type = Internal
        Content = "是否为叶子局？(1: 是；0: 否)"
    }
    let! current, result = requestInputWithMessage msg parseBool
    do! State.put current
    match result with
    | Some b ->
        let r = RollLeaf b |> Draw |> r.SetStatus
        let current = r |> Roll |> current.SetStatus
        do! State.put current
        return current, false
    | None -> return current, false
}

let rollReset (r : RollState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    let remainingBar = getRemainingBar r
    let remainingBoom = getRemainingBoom r
    let resetRolls = getResetRolls (remainingBar.Length > 0) (remainingBoom.Length > 0) r
    if resetRolls.Length = 0 then
        let r = SetLeaf |> r.SetStatus
        let current = r |> Roll |> current.SetStatus
        do! State.put current
        return current, false
    else
        
    let msg = {
        Type = Internal
        Content = "输入需要重抽身份的玩家，输入 0 以继续"
    }
    let parser input =
        let result = parseInt input
        match result with
        | Ok i ->
            if i <= 0 then
                Ok 0
            else
                
            let pId = PlayerId i
            let p = r.Rolls |> List.tryFind (fun s -> s.Player.Id = pId)
            match p with
            | Some player ->
                if resetRolls |> List.exists (fun s -> s = player) then
                    Ok i
                else
                    Error $"玩家 {player.Player.ToCliString()} 无法重抽身份"
            | None -> Error $"玩家 {pId} 不存在"
        | Error e -> Error e
    let! current, result = requestInputWithMessage msg parser
    do! State.put current
    match result with
    | Some 0 ->
        let r = SetLeaf |> r.SetStatus
        let current = r |> Roll |> current.SetStatus
        do! State.put current
        return current, false
    | Some i ->
        let pId = PlayerId i
        let p = r.Rolls |> List.find (fun s -> s.Player.Id = pId)
        let camp = p.Type.GetCamp()
        let pool = if (camp = Bar) then
                        remainingBar
                   else
                        remainingBoom
        let newChara = pool |> List.randomShuffle |> List.head
        sendMessage { Type = ToPlayer p.Player ; Content = newChara.ToString() }
        let newP = { p with Type = newChara ; Reset = true }
        let newRolls = r.Rolls |> List.map (fun s ->
            if s = p then newP else s
        )
        let r = newRolls |> r.SetRolls
        let current = r |> Roll |> current.SetStatus
        do! State.put current
        return current, false
    | None -> return current, false
}

let rollSetLeaf (r : RollState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    let ye = r.Rolls |> List.tryFind (fun s -> s.Type = Leaf)
    match ye with
    | None -> return current, true
    | Some leaf ->
        let msg = { Type = ToPlayer leaf.Player ; Content = "输入叶子的四个身份" }
        let isInvalidCharas c = c = FenXia || c = CaiMon || c = Zombie || c = Leaf
        let parser input =
            let cList = parseCharaList input
            match cList with
            | Ok list ->
                if list.Length <> 4 then
                    Error "请输入四个不重复的身份"
                elif list |> List.exists isInvalidCharas then
                    Error $"无效的身份：{list |> List.find isInvalidCharas}"
                elif (list |> List.filter (fun c -> c.GetCamp() = Bar)).Length = 4
                     || (list |> List.filter (fun c -> c.GetCamp() = Boom)).Length = 4 then
                    Error "必须同时包含吧方和爆方身份"
                else
                    Ok list
            | Error e -> Error e
        let! current, result = requestInputWithMessage msg parser
        do! State.put current
        match result with
        | Some list ->
            let list = list |> List.randomShuffle
            sendMessage { Type = ToPlayer leaf.Player ; Content = $"第一身份：{list.Head}" }
            let r = r.SetLeafRolls list
            let r = r.SetStatus ResetLeaf
            let current = r |> Roll |> current.SetStatus
            do! State.put current
            return current, false
        | None -> return current, false
}

let rollResetLeaf (r : RollState) : State<GameStack, GameStack * bool> = monad {
    let! current = State.get
    let ye = r.Rolls |> List.tryFind (fun s -> s.Type = Leaf)
    match ye with
    | None -> return current, true
    | Some leaf ->
        let msg = { Type = ToPlayer leaf.Player ; Content = "是否重抽第一身份？（1：重抽；0：放弃）" }
        let! current, result = requestInputWithMessage msg parseBool
        do! State.put current
        match result with
            | Some true ->
                let list = r.LeafRolls |> List.randomShuffle
                sendMessage { Type = ToPlayer leaf.Player ; Content = $"第一身份：{list.Head}" }
                let r = r.SetLeafRolls list
                let current = r |> Roll |> current.SetStatus
                do! State.put current
                return current, true
            | Some false -> return current, true
            | None -> return current, false
}

let rollStep (r : RollState) : State<GameStack, GameStack * bool> = monad {
    let! current, result =
        match r.Status with
        | RollStatus.Init -> rollInit r
        | Draw rollLeaf -> rollDraw r rollLeaf
        | AskLeaf -> rollAskLeaf r
        | Reset -> rollReset r
        | SetLeaf -> rollSetLeaf r
        | ResetLeaf -> rollSetLeaf r
            
    do! State.put current
    return current, result
}
