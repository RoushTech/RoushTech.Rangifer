namespace RoushTech.Rangifer.Tests.Mail;

using Microsoft.Extensions.FileProviders;
using RoushTech.Rangifer.Mail;

public enum TestTemplate
{
    Welcome,
}

public enum BrokenTemplate
{
    Broken,
}

public enum MissingTemplate
{
    Missing,
}

internal static class TestTemplates
{
    public static readonly IFileProvider Files = EmailTemplateFiles.Embedded(typeof(TestTemplates).Assembly, "Mail.Templates");
}
