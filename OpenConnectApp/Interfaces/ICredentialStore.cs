namespace OpenConnectApp.Interfaces;

public interface ICredentialStore
{
    void Save(string username, string password);
    (string Username, string Password)? Load();
    void Clear();
}
