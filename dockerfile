FROM ubuntu:22.04 AS builder

# install the .NET 6 SDK from the Ubuntu archive
# (no need to clean the apt cache as this is an unpublished stage)
RUN apt-get update && apt-get install -y dotnet7 ca-certificates

COPY . .

# publish your .NET app
RUN dotnet build -c Release -o /app ./src/add-pr-description/add-pr-description.csproj

FROM ubuntu/dotnet-runtime:7.0-23.04_edge

WORKDIR /app
COPY --from=builder /app ./

ENTRYPOINT ["dotnet", "/app/add-pr-description.dll"]