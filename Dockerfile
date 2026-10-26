FROM node:24-alpine@sha256:44fce696f38ba8c987c4db89954051000bbd05ffd8d8659ba15ed4a8602e937f AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM node:24-alpine@sha256:44fce696f38ba8c987c4db89954051000bbd05ffd8d8659ba15ed4a8602e937f AS whatsapp
WORKDIR /src/whatsapp-gateway
COPY whatsapp-gateway/package*.json ./
RUN npm ci --omit=dev
COPY whatsapp-gateway/src/ ./src/

FROM node:24-bookworm-slim AS node_runtime

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS backend
WORKDIR /src
COPY backend/RoomRental.Api/RoomRental.Api.csproj backend/RoomRental.Api/
RUN dotnet restore backend/RoomRental.Api/RoomRental.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/RoomRental.Api/RoomRental.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
WORKDIR /app
COPY --from=node_runtime /usr/local/bin/node /usr/local/bin/node
COPY --from=backend /out/ ./
COPY --from=frontend /src/frontend/dist/ ./wwwroot/
COPY --from=whatsapp /src/whatsapp-gateway/ ./whatsapp-gateway/
RUN mkdir -p /app/data/keys /app/data/whatsapp-sessions && chown -R app:app /app/data /app/whatsapp-gateway
USER app
ARG MONITORING_ALERT_TO=""
# Recipient only; SMTP credentials must remain runtime configuration.
ENV MONITORING_ALERT_TO=${MONITORING_ALERT_TO}
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RoomRental.Api.dll"]
