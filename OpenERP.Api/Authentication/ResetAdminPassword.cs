using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

        var configuration = WebApplication.CreateBuilder([]).Configuration;
        var connectionString = configuration.GetConnectionString("openerp");
        if (string.IsNullOrWhiteSpace(connectionString))
            return Fail("未配置 ConnectionStrings:openerp。");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
            await using var db = new AppDbContext(options);
            var store = new AccountStore(db, options, new PasswordHasher<AdminAccount>(), TimeProvider.System);
            if (!await store.ResetPasswordAsync(password!, CancellationToken.None))
                return Fail("数据库中没有管理员账号。");
        }
        catch (Exception exception) when (AuthenticationEndpoints.IsDatabaseFailure(exception))
        {
            return Fail("无法重置管理员密码，请检查 PostgreSQL 连接和迁移状态。");
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
