/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AchievementCaretaker.SteamApi;

public class Client : IDisposable
{
    public Wrappers.SteamClient018 SteamClient;
    public Wrappers.SteamUser012 SteamUser;
    public Wrappers.SteamUserStats013 SteamUserStats;
    public Wrappers.SteamUtils005 SteamUtils;
    public Wrappers.SteamApps001 SteamApps001;
    public Wrappers.SteamApps008 SteamApps008;

    private bool _isDisposed = false;
    private int _pipe;
    private int _user;

    private readonly List<ICallback> _callbacks = new();

    public void Initialize(long appId)
    {
        if (string.IsNullOrEmpty(Steam.GetInstallPath()) == true)
        {
            throw new ClientInitializeException(ClientInitializeFailure.GetInstallPath, "failed to get Steam install path");
        }

        if (appId != 0)
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));
        }

        if (Steam.Load() == false)
        {
            throw new ClientInitializeException(ClientInitializeFailure.Load, "failed to load SteamClient");
        }

        SteamClient = Steam.CreateInterface<Wrappers.SteamClient018>("SteamClient018");
        if (SteamClient == null)
        {
            throw new ClientInitializeException(ClientInitializeFailure.CreateSteamClient, "failed to create ISteamClient018");
        }

        _pipe = SteamClient.CreateSteamPipe();
        if (_pipe == 0)
        {
            throw new ClientInitializeException(ClientInitializeFailure.CreateSteamPipe, "failed to create pipe");
        }

        _user = SteamClient.ConnectToGlobalUser(_pipe);
        if (_user == 0)
        {
            throw new ClientInitializeException(ClientInitializeFailure.ConnectToGlobalUser, "failed to connect to global user");
        }

        SteamUtils = SteamClient.GetSteamUtils004(_pipe);
        if (appId > 0 && SteamUtils.GetAppId() != (uint)appId)
        {
            throw new ClientInitializeException(ClientInitializeFailure.AppIdMismatch, "appID mismatch");
        }

        SteamUser = SteamClient.GetSteamUser012(_user, _pipe);
        SteamUserStats = SteamClient.GetSteamUserStats013(_user, _pipe);
        SteamApps001 = SteamClient.GetSteamApps001(_user, _pipe);
        SteamApps008 = SteamClient.GetSteamApps008(_user, _pipe);
    }

    ~Client()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed == true)
        {
            return;
        }

        if (SteamClient != null && _pipe > 0)
        {
            if (_user > 0)
            {
                SteamClient.ReleaseUser(_pipe, _user);
                _user = 0;
            }

            SteamClient.ReleaseSteamPipe(_pipe);
            _pipe = 0;
        }

        _isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Lets go of the client without telling Steam, for when Steam is gone.
    /// </summary>
    /// <remarks>
    /// Releasing the user and the pipe are calls into Steam's library over a pipe
    /// with nothing left on the other end, so they are not made, here or from the
    /// finalizer. There is nothing to release: the process that held it has ended.
    /// </remarks>
    public void Abandon()
    {
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    public TCallback CreateAndRegisterCallback<TCallback>()
        where TCallback : ICallback, new()
    {
        TCallback callback = new();
        _callbacks.Add(callback);
        return callback;
    }

    private bool _runningCallbacks;

    public void RunCallbacks(bool server)
    {
        if (_runningCallbacks == true)
        {
            return;
        }

        _runningCallbacks = true;

        Types.CallbackMessage message;
        while (Steam.GetCallback(_pipe, out message, out _) == true)
        {
            var callbackId = message.Id;
            foreach (ICallback callback in _callbacks.Where(
                candidate => candidate.Id == callbackId &&
                             candidate.IsServer == server))
            {
                callback.Run(message.ParamPointer);
            }
            Steam.FreeLastCallback(_pipe);
        }

        _runningCallbacks = false;
    }
}
