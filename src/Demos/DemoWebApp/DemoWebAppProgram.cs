// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Metering.ClientSDK;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMeteringClientSDK()
    .AddRazorPages();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
