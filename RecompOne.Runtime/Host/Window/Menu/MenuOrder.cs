namespace RecompOne.Runtime.Host.Window;

//now since its at anchor point by name, if two target the same also have to
static class MenuOrder
{
    public static List<T> Arrange<T>(List<T> entries) where T : MenuEntry
    {
        var placed = new List<T>(entries.Count);
        var pending = new List<T>();

        foreach (var entry in entries)
        {
            if (entry.AnchorKey != null && IndexOfKey(entries, entry.AnchorKey) >= 0) pending.Add(entry);
            else placed.Add(entry);
        }

        while (pending.Count > 0)
        {
            bool progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                int at = IndexOfKey(placed, entry.AnchorKey!);
                if (at < 0) continue;

                if (entry.AnchorBefore)
                {
                    while (at > 0 && placed[at - 1].AnchorKey == entry.AnchorKey && placed[at - 1].AnchorBefore) 
                        at--;
                }
                else
                {
                    at++;
                    while (at < placed.Count && placed[at].AnchorKey == entry.AnchorKey &&  !placed[at].AnchorBefore) at++;
                }

                placed.Insert(at, entry);
                pending.RemoveAt(i);
                i--;
                progress = true;
            }
            if (!progress) break;
        }

        placed.AddRange(pending);
        return placed;
    }

    static int IndexOfKey<T>(List<T> list, string key) where T : MenuEntry
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Key == key) return i;
        return -1;
    }
}
