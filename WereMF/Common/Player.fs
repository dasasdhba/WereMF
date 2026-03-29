namespace WereMF.Common

type PlayerId =
    | PlayerId of int
    member this.ToInt() =
        let (PlayerId id) = this
        id
    member this.Reverse() =
        let id = this.ToInt()
        -id |> PlayerId
    override this.ToString() =
        this.ToInt().ToString()
    member this.ToCircleString() =
        let circles = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳㉑㉒㉓㉔㉕㉖㉗㉘㉙㉚㉛㉜㉝㉞㉟㊱㊲㊳㊴㊵㊶㊷㊸㊹㊺㊻㊼㊽㊾㊿"
        if this > PlayerId 0 && this <= PlayerId 50 then
            string circles[this.ToInt() - 1]
        else
            this.ToString()

type Player =
    {
        Id : PlayerId
        BaseName : string
        Anonymous : bool
    }
    static member New id name =
        { Id = id ; BaseName = name ; Anonymous = false }
    member this.Name =
        if this.Anonymous then $"玩家{this.Id}" else this.BaseName
    member this.ToCliString() =
        $"{this.Id.ToString()}: {this.BaseName}"
    member this.ToInGameString() =
        $"{this.Id.ToCircleString()}{this.Name}"