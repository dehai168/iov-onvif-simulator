using Iov.OnvifSimulator.Models;
using SharpOnvifCommon.Security;
using SharpOnvifServer;

namespace Iov.OnvifSimulator.Onvif;

public sealed class SimulatorUserRepository : IUserRepository
{
    private readonly SimulatorConfig _config;

    public SimulatorUserRepository(SimulatorConfig config)
    {
        _config = config;
    }

    public UserInfo GetUser(string userName)
    {
        if (!_config.EnableAuthentication)
        {
            return new UserInfo(userName, string.Empty);
        }

        if (string.Equals(userName, _config.UserName, StringComparison.Ordinal))
        {
            return new UserInfo(_config.UserName, _config.Password);
        }

        return null!;
    }

    public Task<UserInfo> GetUserAsync(string userName)
    {
        return Task.FromResult(GetUser(userName));
    }

    public UserInfo GetUserByHash(string algorithm, string userName, string realm)
    {
        var hashed = HttpDigestAuthentication.CreateUserNameHashRFC7616(algorithm, _config.UserName, realm);
        if (string.Equals(hashed, userName, StringComparison.OrdinalIgnoreCase))
        {
            return new UserInfo(_config.UserName, _config.Password);
        }

        return null!;
    }

    public Task<UserInfo> GetUserByHashAsync(string algorithm, string userName, string realm)
    {
        return Task.FromResult(GetUserByHash(algorithm, userName, realm));
    }
}
