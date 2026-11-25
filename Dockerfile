# Generic multi-stage build for any service:
#   docker build --build-arg PROJECT=src/Services/Booking/Pss.Booking.Api/Pss.Booking.Api.csproj --build-arg DLL=Pss.Booking.Api.dll .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG DLL
ENV APP_DLL=$DLL
ENV ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet $APP_DLL"]
