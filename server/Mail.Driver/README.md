# SeliseBlocks.MailDriver

## Overview

`SeliseBlocks.MailDriver` is a Mail driver designed to integrate email functionalities into your application. It provides a standardized way to send mail securely.

## Installation

To install `SeliseBlocks.MailDriver`, add the NuGet package to your project:

```sh
dotnet add package SeliseBlocks.MailDriver
```

## Usage

### Register Dependencies

Before using `SeliseBlocks.MailDriver`, ensure that all required dependencies are registered in your application's dependency injection container. Add the following line in your `Program.cs`:

add the namespace

```csharp
using Blocks.Extension.DependencyInjection;
```

register the service

```csharp
builder.Services.RegisterBlocksMailService();
```

This method will configure and register all necessary services required for the Mail driver to function properly.

## Features

- Send mail

  ```csharp
  var request = new SendMail
  {
      SubjectDataContext = <Dictionary<string, string>>,
      To = <string[]>,
      Bcc = <string[]>,
      Cc = <string[]>,
      Purpose = <string>,
      Language = <string>,
      ReplyTo = <string[]>,
      Attachments = <string[]>,
      BodyDataContext = <Dictionary<string, string>>,
      ProjectKey = <string>
  }
  ```

  Invoke `SendAsync`

  ```csharp
  await SendAsync(request);
  ```
