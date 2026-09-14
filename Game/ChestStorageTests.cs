using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private async Task RunChestStorageTest(string output,bool verify)
    {
        var results=new List<string>();void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);GD.Print(results[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(10);
            if(verify)
            {
                var saved=state.Chests.Single(c=>c.Id=="item-test");
                Check(saved.Name=="Building supplies" && saved.Items.OfType<ChestStack>().Any(s=>s.Kind=="stone" && s.Count==64),"Item chest name and stack survive fresh process");
                Check(saved.Items.Any(s=>s?.ItemId=="stored-photo") && !SupplyItems().Any(i=>i.Id=="stored-photo"),"Stored photo remains owned by chest after restart");
                ShowChest(saved.Id);await Frames(6);Check(Descendants(panel!).OfType<ChestSlot>().Any(s=>s.Name=="ItemChestSlot0"),"Reopened item chest selects item UI automatically");
            }
            else
            {
                var chest=new Chest("item-test","Building supplies",0,0);state.Chests.Add(chest);BuildWorld();
                state.Supplies["stone"]=128;state.HotbarStacks=new string?[9];state.Hotbar=new string?[9];BuildingItems();
                int source=BuildingItems().FindIndex(i=>i.Kind=="stone");StoreChestStack(chest.Id,0,"$slot:"+source);
                Check(chest.Items[0]?.Count==64 && state.Supplies["stone"]==64,"Deposit transfers a stack without copying it");
                var slot=Descendants(panel!).OfType<ChestSlot>().Single(s=>s.Name=="ItemChestSlot1");slot._DropData(Vector2.Zero,new Godot.Collections.Dictionary {{"buildkey","$chest:item-test:0"}});
                Check(chest.Items[0]==null && chest.Items[1]?.Count==64,"Dragging within chest moves the stack");
                TakeChestStack(chest.Id,1,"$hotbar:0");Check(state.Supplies["stone"]==128 && !ChestStorage.HasItems(chest) && state.Hotbar[0]=="stone","Withdrawal restores owned stack to hotbar");
                StoreChestStack(chest.Id,0,"$hotbar:0");
                int mossSlot=BuildingItems().FindIndex(i=>i.Kind=="moss");DropHotbarItem("$slot:"+mossSlot,1);
                StoreChestStack(chest.Id,0,"$hotbar:1");
                Check(chest.Items[0]?.Kind=="moss" && state.Hotbar[1]=="stone","Deposit onto occupied chest slot swaps with hotbar");
                TakeChestStack(chest.Id,0,"$hotbar:1");
                Check(chest.Items[0]?.Kind=="stone" && state.Hotbar[1]=="moss","Withdrawal onto occupied hotbar swaps in reverse");
                bool rejected=false;try{library.Add(System.IO.Path.Combine(output,"fixture.txt"),chest.Id);}catch(InvalidOperationException){rejected=true;}
                Check(rejected && !state.Links.Any(l=>l.ChestId==chest.Id),"File links cannot enter an item chest");
                var fileChest=new Chest("file-test","Files",1,0);state.Chests.Add(fileChest);library.Add(System.IO.Path.Combine(output,"fixture.txt"),fileChest.Id);
                Check(!ChestStorage.AcceptsItems(state,fileChest.Id),"Existing file chest rejects Minecraft items");
                library.Remove(state.Links.Single(l=>l.ChestId==fileChest.Id).Id);Check(ChestStorage.AcceptsItems(state,fileChest.Id),"Empty chest can change type");
                var photo=new Decoration("item_frame",0,0){Id="stored-photo",Carried=true,PicturePath=System.IO.Path.Combine(output,"photo.png")};state.Decorations.Add(photo);BuildingItems();
                StoreChestStack(chest.Id,2,"$slot:"+BuildingItems().FindIndex(i=>i.Id==photo.Id));
                Check(chest.Items[2]?.ItemId==photo.Id && !SupplyItems().Any(i=>i.Id==photo.Id),"Photo frame identity is stored without inventory duplication");
                var outer=new Chest("outer-test","Outer",0,0){Carried=true};state.Chests.Add(outer);outer.Items.Add(new ChestStack {Kind="chest",ItemId=chest.Id});
                bool cycle=false;try{ChestStorage.ValidateItem(state,chest,new ChestStack {Kind="chest",ItemId=outer.Id});}catch(InvalidOperationException){cycle=true;}
                Check(cycle,"Nested chests cannot create a containment cycle");outer.Items.Clear();state.Chests.Remove(outer);
                ShowItemChest(chest.Id);await Frames(6);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"item-chest.png"));
                Changed();Check(!dirty,"Item chest saves successfully");
            }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(output,verify?"read.txt":"write.txt"),results);
        }
        catch(Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(output,"failure.txt"),e.ToString());GD.PushError(e.ToString());}
        GetTree().Quit();
    }
}
