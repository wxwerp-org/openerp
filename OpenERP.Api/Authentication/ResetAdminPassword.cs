namespace OpenERP.Api.Authentication;

internal interface IAdministratorPasswordReset
{
    Task<bool> ResetPasswordAsync(string password, CancellationToken cancellationToken);
}

internal static class ResetAdminPassword
{
    internal static async Task<string?> ReadNewPasswordAsync(string[] args)
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
                    return InvalidInput("两次输入的密码不一致。");
                break;
            default:
                return InvalidInput("使用 reset-admin-password 交互输入，或使用 reset-admin-password --stdin 从重定向的标准输入读取一行密码。");
        }

        if (!AccountStore.IsValidPassword(password))
            return InvalidInput("新密码长度必须为 6–20 个字符。");

        return password;
    }

    internal static async Task<int> RunAsync(IAdministratorPasswordReset accounts, string password)
    {
        if (!await accounts.ResetPasswordAsync(password, CancellationToken.None))
        {
            Console.Error.WriteLine("数据库中没有管理员账号。");
            return 1;
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

    private static string? InvalidInput(string message)
    {
        Console.Error.WriteLine(message);
        return null;
    }
}
