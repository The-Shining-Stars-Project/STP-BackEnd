using CRM.Domain.Common;

namespace CRM.Domain.Entities;

// Master onboarding checklist. Copied onto each new staff member at creation, and since
// Sep 2026 also synced onto existing staff when it is edited (new items appear, removed
// items go unless already completed or carrying a file).
public class ChecklistTemplateItem : BaseEntity
{
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>
    /// For items that must be renewed (TB every 4 years, CPR and harassment training every
    /// 2): the interval in months. Completing the item stamps its expiry from the completion
    /// date. Null for one-time items.
    /// </summary>
    public int? RenewalMonths { get; set; }
}
