namespace Ash.Modules.Cases.Application.Agenda;

/// <summary>
/// Hearings of the same lawyer within <see cref="Window"/> of each other, in one pass: each lawyer's hearings sorted
/// by time, each compared only with the following ones until the gap exceeds the window.
/// </summary>
public static class AgendaConflicts
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    /// <param name="hearings">Non-cancelled hearings with the lawyers they count for.</param>
    /// <returns>Per hearing id, the ids of the hearings it conflicts with (only hearings that have a conflict).</returns>
    public static Dictionary<ulong, HashSet<ulong>> Find(IEnumerable<(ulong HearingId, DateTime At, IEnumerable<uint> Lawyers)> hearings)
    {
        var perLawyer = new Dictionary<uint, List<(ulong Id, DateTime At)>>();
        foreach (var (id, at, lawyers) in hearings)
        {
            foreach (var lawyer in lawyers)
            {
                if (!perLawyer.TryGetValue(lawyer, out var list))
                {
                    perLawyer[lawyer] = list = [];
                }

                list.Add((id, at));
            }
        }

        var result = new Dictionary<ulong, HashSet<ulong>>();
        foreach (var list in perLawyer.Values)
        {
            list.Sort((a, b) => a.At.CompareTo(b.At));
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count && list[j].At - list[i].At <= Window; j++)
                {
                    Link(result, list[i].Id, list[j].Id);
                    Link(result, list[j].Id, list[i].Id);
                }
            }
        }

        return result;
    }

    private static void Link(Dictionary<ulong, HashSet<ulong>> result, ulong from, ulong to)
    {
        if (!result.TryGetValue(from, out var set))
        {
            result[from] = set = [];
        }

        set.Add(to);
    }
}
