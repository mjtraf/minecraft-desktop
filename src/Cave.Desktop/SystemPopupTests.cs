namespace Cave.Desktop;
internal sealed partial class DesktopContext
{
    private async Task RunSystemPopupTest(string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();var previous=Cursor.Position;
        void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
        try
        {
            for(int i=0;i<150 && window==0;i++)await Task.Delay(100);
            if(window==0)throw new InvalidOperationException("Renderer did not attach.");
            Enter();await Task.Delay(400);OpenSystem("system");await Task.Delay(200);
            var popup=systemPopup!;
            Check(popup.Visible && !active,"System hands control away from cave before showing");
            Enter();await Task.Delay(250);
            Check(!popup.IsDisposed && popup.Visible && Native.GetForegroundWindow()==popup.Handle,"Queued cave focus request does not dismiss System");
            foreach(var point in new[]{new Point(popup.Left+30,popup.Top+30),new Point(popup.Right-30,popup.Bottom-30),new Point(popup.Left-15,popup.Bottom+15)})
            {Cursor.Position=point;await Task.Delay(250);}
            Check(!popup.IsDisposed && popup.Visible,"Moving across and outside System keeps it open");
            Native.SetForegroundWindow(window);await Task.Delay(300);
            Check(popup.IsDisposed,"Switching focus outside dismisses System");
        }
        catch(Exception e){results.Add("FAIL "+e);}
        finally{Cursor.Position=previous;File.WriteAllLines(Path.Combine(output,"results.txt"),results);Close();}
    }
}
