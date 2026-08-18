namespace RustPlusHelper.Application.RustPlus;

public interface ICompanionEventRepository
{
    IReadOnlyList<CompanionEvent> GetRecent(Guid serverId, int limit);

    void Append(CompanionEvent companionEvent, int retentionLimit);
}
