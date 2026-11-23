# Mail

Email templates, sending, and Resend webhook checks.

Fluid writes `{{ name }}` into the HTML as-is unless you give it an encoder, so a name like `</title><a href=...>` ends up as markup in the email. Rangifer always encodes, so bugs like this are fixed once.

```bash
dotnet add package RoushTech.Rangifer.Mail
```

## Templates

Each template starts with frontmatter holding its subject:

```liquid
---
subject: "{{ inviter.name }} invited you to {{ team.name }}"
---
{% layout '_layout.liquid' %}
<p>{{ inviter.name }} invited you to <strong>{{ team.name }}</strong>.</p>
```

The layout gets the rendered subject as `{{ subject }}`, and its `<style>` is inlined into the HTML.

Every value is HTML-encoded, so don't add `| escape`: it encodes a second time. For markup built in code, pass an `HtmlFragment`. Templates can use `| linkify` to turn plain text into HTML with its URLs linked.

## Rendering

```csharp
var files = EmailTemplateFiles.Embedded(typeof(Program).Assembly);
var templates = new TransactionalTemplates<EmailTemplate>(files);
var renderer = new LiquidEmailRenderer(files);

var template = templates.Get(EmailTemplate.Invite);
var email = await renderer.RenderAsync(template.Subject, template.Body, TemplateModel.From(variables));
```

`TransactionalTemplates` loads `<Member>.liquid` for every enum member at startup and throws if one is missing or doesn't parse.

Variables stored as JSON go back through `TemplateModel.FromJson`. Write them with `TemplateModel.ToJson` so `HtmlFragment` values survive.

## Sending

`SmtpEmailTransport` sends a MimeKit message and returns the server's response. Your app builds the message.

```csharp
var message = new MimeMessage { Subject = email.Subject, Body = new TextPart("html") { Text = email.Html } };
OneClickUnsubscribe.AddTo(message, unsubscribeUrl);
await transport.SendAsync(message);
```

## Resend webhooks

`SvixSignature.Problem` checks a webhook's `svix-id`, `svix-timestamp` and `svix-signature` headers against the signing secret, and says why it was refused.
