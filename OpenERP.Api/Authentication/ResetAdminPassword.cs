namespace OpenERP.Api.Authentication;

internal static class ResetAdminPassword
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string? password;
        switch (args)
        {
            case ["reset-admin-password", "--stdin"] when Console.IsInputRedirected:
                password = await Console.In.ReadLineAsync();
                break;
            case ["reset-admin-password"] when !Console.IsInputRedirected:
                password = ReadPassword("新密码：");
                if (password != ReadPassword("确认新密码："))
                    return Fail("两次输入的密码不一致。");
                break;
            default:
                return Fail("使用 reset-admin-password 交互输入，或使用 reset-admin-password --stdin 从重定向的标准输入读取一行密码。");
        }

        if (!AccountStore.IsValidPassword(password))
            return Fail("新密码长度必须为 6–20 个字符。");

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("openerp")))
            return Fail("未配置 ConnectionStrings:openerp。");

        try
        {
            builder.AddApplicationDatabase();
            builder.Services.AddAccountStore();
            await using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AccountStore>();
            var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
            if (!await store.ResetPasswordAsync(password!, lifetime.ApplicationStopping))
                return Fail("数据库中没有管理员账号。");
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return Fail("无法重置管理员密码，请检查数据库连接和 migrations 状态。");
        }

        Console.WriteLine("管理员密码已重置，所有登录会话已撤销。");
        return 0;
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
            }
            else if (key.KeyChar != '\0')
            {
                password.Append(key.KeyChar);
            }
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
