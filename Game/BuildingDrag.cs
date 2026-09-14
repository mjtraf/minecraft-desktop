using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private List<(string Kind,string? Id,string Name,int Count)> OrderBuildingItems(List<(string Kind,string? Id,string Name,int Count)> items)
    {
        state.BuildingSlots ??=[];
        var counts=new Dictionary<string,int>();var map=new Dictionary<string,(string Kind,string? Id,string Name,int Count)>();
        foreach(var item in items) {string key=item.Id??item.Kind;int n=counts.GetValueOrDefault(key);counts[key]=n+1;map[key+":"+n]=item;}
        state.HotbarStacks ??=new string?[9];
        if(!state.HotbarOwnsStacks)
        {
            state.HotbarOwnsStacks=true;var assigned=new HashSet<string>();
            for(int i=0;i<9;i++) {var key=state.Hotbar[i];var token=map.FirstOrDefault(p=>(p.Value.Id??p.Value.Kind)==key && !assigned.Contains(p.Key)).Key;state.HotbarStacks[i]=token;if(token!=null) assigned.Add(token);}
        }
        for(int i=0;i<9;i++) if(state.HotbarStacks[i] is {} token && !map.ContainsKey(token)) {state.HotbarStacks[i]=null;state.Hotbar[i]=null;}
        UpdateHotbarKeys();
        var held=state.HotbarStacks.Where(k=>k!=null).ToHashSet();
        foreach(var key in held) map.Remove(key!);
        var used=new HashSet<string>();
        for(int i=0;i<state.BuildingSlots.Count;i++) {var key=state.BuildingSlots[i];if(key==null || !map.ContainsKey(key) || !used.Add(key)) state.BuildingSlots[i]=null;}
        foreach(var key in map.Keys.Where(k=>!used.Contains(k))) {int empty=state.BuildingSlots.IndexOf(null);if(empty<0) state.BuildingSlots.Add(key);else state.BuildingSlots[empty]=key;}
        return state.BuildingSlots.Select(key=>key!=null?map[key]:("",(string?)null,"Empty",0)).ToList();
    }
    private void UpdateHotbarKeys()
    {
        for(int i=0;i<9;i++) {var token=state.HotbarStacks[i];state.Hotbar[i]=token==null?null:token[..token.LastIndexOf(':')];}
    }
    private void DropHotbarItem(string source,int target)
    {
        var items=BuildingItems();if(target<0 || target>=9) return;
        if(source.StartsWith("$hotbar:") && int.TryParse(source[8..],out int bar) && bar>=0 && bar<9) (state.HotbarStacks[bar],state.HotbarStacks[target])=(state.HotbarStacks[target],state.HotbarStacks[bar]);
        else
        {
            int from=-1;
            if(source.StartsWith("$slot:")) {if(!int.TryParse(source[6..],out from)) return;}
            else {string? key=ReceiveBuildingDrag(source);if(key==null) return;items=BuildingItems();from=items.FindLastIndex(i=>(i.Id??i.Kind)==key);if(from<0) {int existing=Array.IndexOf(state.Hotbar,key);if(existing>=0) {DropHotbarItem("$hotbar:"+existing,target);return;}}}
            if(from<0 || from>=state.BuildingSlots.Count) return;
            (state.BuildingSlots[from],state.HotbarStacks[target])=(state.HotbarStacks[target],state.BuildingSlots[from]);
        }
        UpdateHotbarKeys();Changed();
    }
    private void BindHotbarSupply(ChestSlot slot,int index)
    {
        BuildingItems();BindSupply(slot,state.Hotbar[index]);slot.BuildDragKey="$hotbar:"+index;
        if(state.HotbarStacks[index] is {} token && int.TryParse(token[(token.LastIndexOf(':')+1)..],out int stack)) slot.Count=Math.Min(64,Math.Max(0,(state.Supplies.GetValueOrDefault(slot.BuildKind??"",1)-64*stack)));
    }
    private string? ReceiveBuildingDrag(string source)
    {
        if(source.StartsWith("$catalog:")) {string kind=source[9..];if(!BlockNames.ContainsKey(kind)) return null;state.Supplies[kind]=state.Supplies.GetValueOrDefault(kind)+64;Changed();return kind;}
        if(source.StartsWith("$slot:") && int.TryParse(source[6..],out int index)) {var items=BuildingItems();if(index<0 || index>=items.Count || items[index].Count==0) return null;return items[index].Id??items[index].Kind;}
        return ResolveSupply(source)!=null?source:null;
    }
    private void DropBuildingItem(string source,int target)
    {
        if(target<0) return;
        BuildingItems();
        if(source.StartsWith("$hotbar:") && int.TryParse(source[8..],out int bar) && bar>=0 && bar<9) {while(state.BuildingSlots.Count<=target) state.BuildingSlots.Add(null);(state.BuildingSlots[target],state.HotbarStacks[bar])=(state.HotbarStacks[bar],state.BuildingSlots[target]);UpdateHotbarKeys();Changed();return;}
        int from=-1;
        if(source.StartsWith("$slot:")) int.TryParse(source[6..],out from);
        else if(source.StartsWith("$catalog:"))
        {
            string? kind=ReceiveBuildingDrag(source);if(kind==null) return;
            var items=BuildingItems();from=items.FindLastIndex(i=>i.Kind==kind);
        }
        else {string? key=ReceiveBuildingDrag(source);if(key!=null) from=BuildingItems().FindIndex(i=>(i.Id??i.Kind)==key);}
        if(from<0 || from>=state.BuildingSlots.Count) return;
        while(state.BuildingSlots.Count<=target) state.BuildingSlots.Add(null);
        (state.BuildingSlots[from],state.BuildingSlots[target])=(state.BuildingSlots[target],state.BuildingSlots[from]);Changed();
    }
}
