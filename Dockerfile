# =========================================================================================
# STAGE 1: BUILD & PUBLISH (.NET 10 SDK)
# =========================================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Sao chép file Solution và Project trước để tối ưu Docker Layer Cache khi restore
COPY ["BookShare.sln", "./"]
COPY ["src/BookShare.Web/BookShare.Web.csproj", "src/BookShare.Web/"]
RUN dotnet restore "src/BookShare.Web/BookShare.Web.csproj"

# Sao chép toàn bộ mã nguồn của BookShare.Web và Publish ở chế độ Release
COPY ["src/BookShare.Web/", "src/BookShare.Web/"]
WORKDIR "/src/src/BookShare.Web"
RUN dotnet publish "BookShare.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =========================================================================================
# STAGE 2: FINAL RUNTIME IMAGE (.NET 10 ASP.NET CORE RUNTIME - PORT 5087)
# =========================================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Tạo sẵn thư mục lưu ảnh biên bản tình trạng sách (Dịch vụ S10 ConditionReport)
RUN mkdir -p /app/wwwroot/uploads/conditions

# Sao chép bản dựng đã publish từ Stage 1
COPY --from=build /app/publish .

# Cố định cổng nội bộ 5087 phục vụ Nginx Reverse Proxy
ENV ASPNETCORE_URLS=http://+:5087
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5087

ENTRYPOINT ["dotnet", "BookShare.Web.dll"]
