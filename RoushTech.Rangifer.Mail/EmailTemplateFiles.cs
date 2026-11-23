namespace RoushTech.Rangifer.Mail;

using System.Reflection;
using Microsoft.Extensions.FileProviders;

public static class EmailTemplateFiles
{
    /// <param name="folder">Dotted for nested folders, like <c>Mail.Templates</c>.</param>
    public static IFileProvider Embedded(Assembly assembly, string folder = "Templates") =>
        new EmbeddedFileProvider(assembly, $"{assembly.GetName().Name}.{folder}");

    public static string Read(IFileProvider files, string path)
    {
        var file = files.GetFileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidOperationException($"Email template '{path}' not found.");
        }

        using var reader = new StreamReader(file.CreateReadStream());
        return reader.ReadToEnd();
    }
}
