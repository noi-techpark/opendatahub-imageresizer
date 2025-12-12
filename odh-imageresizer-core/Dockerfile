#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# install System.Drawing native dependencies
#RUN apt-get update \
    #&& apt-get install -y --allow-unauthenticated \
        #libc6-dev \
        #libgdiplus \
        #libx11-dev \
     #&& rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0.407 AS build
WORKDIR /src
COPY ["odh-imageresizer-core/odh-imageresizer-core.csproj", "odh-imageresizer-core/"]
RUN dotnet restore "odh-imageresizer-core/odh-imageresizer-core.csproj"
COPY . .
WORKDIR "/src/odh-imageresizer-core"
RUN dotnet build "odh-imageresizer-core.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "odh-imageresizer-core.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "odh-imageresizer-core.dll"]

