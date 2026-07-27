# PlantsRandomizer (PVZ Fusion BepInEx Mod)

**PlantsRandomizer** là một BepInEx Plugin cho tựa game **Plants vs. Zombies Fusion (PVZ Fusion 3.8)** giúp ngẫu nhiên hóa (randomize) tất cả phần thưởng cây khi vượt qua các màn Adventure Mode.

---

## 🌟 Tính năng nổi bật (Features)

- **Default Starter Protection (Cây mặc định)**: Mặc định khi bắt đầu save data mới, player chỉ có **Peashooter (Bắn Đậu)** và **Sunflower (Hướng Dương)**, không bị ngẫu nhiên nhận trùng hay thừa cây khác.
- **Fixed Terrain Progression (Đảm bảo địa hình)**: 
  - **LilyPad (Bèo)** cố định mở khóa tại màn **Pool 1 (3-1)**.
  - **FlowerPot (Chậu)** cố định mở khóa tại màn **Roof 1 (5-1)**.
- **Per-Account/Save Deterministic Seed (Seed riêng theo Save)**: Mỗi slot save / tên người chơi có một seed ngẫu nhiên riêng biệt. Bạn có thể tạo nhiều profile chơi khác nhau với tiến trình ngẫu nhiên hoàn toàn độc lập.
- **Adventure Priority (Ưu tiên cây cơ bản)**: 44 màn Adventure đầu tiên được ưu tiên phân bổ 44 cây cơ bản chính chủ của game gốc, đảm bảo mỗi màn qua đi player luôn mở khóa 1 cây cơ bản mới hiển thị đầy đủ trên trang *"Cây cơ bản"*.
- **Base Game Cards Only (Không dính mod ngoài)**: Pool ngẫu nhiên chỉ lấy cây chính chủ trong game gốc PVZ Fusion (bao gồm cả các cây mở khóa bằng màn đặc biệt như *Hamburger, Pudding, Apple, SniperPea, SuperGatling, DoomCherry...*). Loại bỏ hoàn toàn các cây từ các mod bên ngoài khác.
- **Grid Layout Fix & Developer Mode**: Tự động mở khóa nối tiếp thông minh giúp bảng *"Chọn cây"* luôn lấp đầy 100%, không bị ô trống / lủng lỗ ở giữa. Đồng thời tương thích hoàn toàn với tính năng *"Mở khóa hết cây"* (`developerMode`).

---

## 📦 Cấu trúc thư mục Source Code (Project Structure)

```text
PlantsRandomizer/
├── PlantsRandomizer.csproj    # File dự án .NET 6 SDK C#
├── Plugin.cs                  # Mã nguồn chính của Mod (Harmony Patches & Pool Logic)
├── README.md                  # Tài liệu hướng dẫn
└── .gitignore                 # Cấu hình Git ignore
```

---

## 🛠️ Hướng dẫn Biên dịch (Building from Source)

### Yêu cầu (Prerequisites):
1. **.NET 6.0 SDK** trở lên.
2. Tệp cài đặt **PVZ Fusion 3.8** đã cài đặt **BepInEx IL2CPP**.

### Các bước biên dịch:
1. Clone hoặc tải thư mục `PlantsRandomizer` về máy:
   ```bash
   git clone https://github.com/your-username/PlantsRandomizer.git
   cd PlantsRandomizer
   ```
2. Thực hiện lệnh build bằng .NET CLI:
   ```bash
   dotnet build -c Release
   ```
3. File DLL kết quả sẽ nằm tại: `bin/Release/net6.0/PlantsRandomizer.dll`.

---

## 🎮 Hướng dẫn Cài đặt & Sử dụng (Installation)

1. Đặt tệp `PlantsRandomizer.dll` vào thư mục:
   ```text
   <PVZ_Fusion_Directory>/BepInEx/plugins/
   ```
2. Khởi động game `PlantsVsZombiesRH.exe`. Mod sẽ tự động tạo file mapping tương ứng với profile của bạn trong thư mục `BepInEx/config/PlantsRandomizer_Mapping_<ProfileName>.txt`.

---

## 📜 Giấy phép (License)

Dự án này được phát triển dành riêng cho cộng đồng mod **PVZ Fusion Fanmade**. Mọi đóng góp (Pull Requests / Issues) đều được hoan nghênh!
