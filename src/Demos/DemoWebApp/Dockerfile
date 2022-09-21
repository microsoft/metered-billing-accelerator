#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0.300 AS build
WORKDIR /src
COPY ["Demos/DemoWebApp/DemoWebApp.csproj", "Demos/DemoWebApp/"]
COPY ["Metering.BaseTypes/Metering.BaseTypes.fsproj", "Metering.BaseTypes/"]
COPY ["Metering.Runtime/Metering.Runtime.fsproj", "Metering.Runtime/"]
RUN dotnet restore "Demos/DemoWebApp/DemoWebApp.csproj"
COPY . .
WORKDIR "/src/Demos/DemoWebApp"
RUN dotnet build "DemoWebApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DemoWebApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DemoWebApp.dll"]
