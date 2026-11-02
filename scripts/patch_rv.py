import os

def patch_remote_viewer():
    path = 'src/EnterpriseRemote.Controller/UI/RemoteViewerForm.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read().replace('\r\n', '\n')

    # 1. Update PermanentTokensFilePath to be app-local first
    old_perm = '''    private static string PermanentTokensFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnterpriseRemote",
        "permanent_keys.json");'''

    new_perm = '''    private static string PermanentTokensFilePath
    {
        get
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "permanent_keys.json");
            if (File.Exists(local)) return local;
            try
            {
                string test = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $".wt_{Guid.NewGuid():N}");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return local;
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnterpriseRemote",
                "permanent_keys.json");
        }
    }'''

    assert old_perm in text, "old_perm not found"
    text = text.replace(old_perm, new_perm)

    # 2. Add [Wi-Fi IP] link button
    old_lnk = '''        var lnkLocal = new LinkLabel
        {
            Text = "[Reset 127.0.0.1]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 128, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };'''

    new_lnk = '''        var lnkWifi = new LinkLabel
        {
            Text = "[Wi-Fi IP]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 198, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        lnkWifi.LinkClicked += (_, _) =>
        {
            string wifiIp = EnterpriseRemote.Common.Network.LanDiscoveryUtils.GetPrimaryLanIp();
            _txtServer.Text = $"http://{wifiIp}:5293";
            CyberToast.Show(this, "Wi-Fi Gateway", $"Gateway set to local Wi-Fi: http://{wifiIp}:5293", ToastType.Info, 2500);
        };
        cardGateway.Controls.Add(lnkWifi);

        var lnkLocal = new LinkLabel
        {
            Text = "[Reset 127.0.0.1]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 128, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };'''

    assert old_lnk in text, "old_lnk not found"
    text = text.replace(old_lnk, new_lnk)

    # 3. In AuthenticateAndLoadDevicesAsync: auto-start if local machine
    old_auth = '''                // Only if the user specifically requested localhost / 127.0.0.1 and it's not up, attempt auto-start
                if (serverUrl.Contains("127.0.0.1") || serverUrl.Contains("localhost"))'''

    new_auth = '''                // Auto-start Control Plane if targeting local PC (127.0.0.1, localhost, or host's own Wi-Fi/LAN IP)
                if (serverUrl.Contains("127.0.0.1") || serverUrl.Contains("localhost") || EnterpriseRemote.Common.Network.LanDiscoveryUtils.IsLocalMachine(serverUrl))'''

    assert old_auth in text, "old_auth not found"
    text = text.replace(old_auth, new_auth)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text.replace('\n', '\r\n'))
    print('[+] RemoteViewerForm.cs updated successfully')

if __name__ == '__main__':
    patch_remote_viewer()

