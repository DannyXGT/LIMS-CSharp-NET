using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Lims.DesignSystem.Presentation;

/// <summary>Snapshots a loaded catalogue once; edits only the changed visible entries per query.</summary>
public sealed class LocalSelectFilter
{
    private readonly List<(object Item, string Label)> _source = [];
    public ObservableCollection<object> Visible { get; } = [];

    public void Load(IEnumerable? items, Func<object, string> label)
    {
        _source.Clear();
        if (items is not null)
            foreach (var item in items)
                if (item is not null) _source.Add((item, label(item)));
        Apply(string.Empty);
    }

    public void Apply(string query)
    {
        var target = 0;
        foreach (var (item, label) in _source)
        {
            if (CultureInfo.GetCultureInfo("es-GT").CompareInfo.IndexOf(label, query.Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) continue;
            if (target < Visible.Count && ReferenceEquals(Visible[target], item)) { target++; continue; }
            var existing = Visible.IndexOf(item);
            if (existing >= target) Visible.Move(existing, target);
            else Visible.Insert(target, item);
            target++;
        }
        while (Visible.Count > target) Visible.RemoveAt(Visible.Count - 1);
    }
}
