using System.Text.Json;
namespace Cave.Desktop;

internal static class VillagerMemoryStore
{
    internal static (VillagerMemory Memory,bool Recovered) Load(string path,VillagerMemory initial)
    {
        var errors=new List<Exception>();
        foreach(string candidate in new[]{path,path+".bak"})
        {
            try
            {
                var memory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(candidate))??throw new InvalidDataException("Empty agent save.");
                if(memory.Transcript==null || memory.ProjectThreads==null || string.IsNullOrWhiteSpace(memory.Name))throw new InvalidDataException("Incomplete agent save.");
                return(memory,candidate!=path);
            }
            catch(FileNotFoundException){}
            catch(DirectoryNotFoundException){}
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException){errors.Add(e);}
        }
        if(errors.Count>0)throw new IOException("The saved villager conversation could not be read. It has been preserved; refusing to replace it with a blank session.",errors[0]);
        return(initial,false);
    }
}
