module WereMF.GameState

open WereMF.Entity
open WereMF.Player
open WereMF.RollState

type GameStatus =
    | Init
    | Roll of RollState
    | Night
    | Day
    | End

type GameState =
    {
        Status : GameStatus
        Players : Player list
        Entities : Entity list
    }

let maxUndo = 100
    
type GameStack =
    {
        Current : GameState    
        UndoStack : GameState list
        RedoStack : GameState list
    }
    member this.Status = this.Current.Status
    member this.Players= this.Current.Players
    member this.Entities = this.Current.Entities
    member this.SetStatus(status) =
        { this with Current.Status = status }
    member this.SetPlayers(players) =
        { this with Current.Players = players }
    member this.SetEntities(entities) =
        { this with Current.Entities = entities }
    
    member this.Push() =
        let stack = this.Current :: this.UndoStack
        if stack.Length > maxUndo then
            { this with UndoStack = stack[..(maxUndo - 1)] ; RedoStack = [] }
        else
            { this with UndoStack = stack ; RedoStack = [] }
    member this.Undo() =
        if this.UndoStack.Length = 0 then
            Error "Nothing undo"
        else
            let newState = this.UndoStack.Head
            Ok { this with
                  Current = newState
                  UndoStack = this.UndoStack.Tail
                  RedoStack = this.Current :: this.RedoStack }
    member this.Redo() =
        if this.RedoStack.Length = 0 then
            Error "Nothing redo"
        else
            let newState = this.RedoStack.Head
            Ok { this with
                  Current = newState
                  UndoStack = this.Current :: this.UndoStack
                  RedoStack = this.RedoStack.Tail }

let newGameState = {
    Status = Init
    Players = []
    Entities = []
}

let newGame= {
    Current = newGameState;
    UndoStack = [];
    RedoStack = [];
 }