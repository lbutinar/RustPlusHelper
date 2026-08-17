namespace RustPlusHelper.Application.Servers;

public interface IServerRepository
{
    IReadOnlyList<ServerProfile> GetAll();

    ServerProfile? GetById(Guid id);

    void Upsert(ServerProfile profile);

    bool Remove(Guid id);
}
