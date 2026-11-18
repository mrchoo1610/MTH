Khi đang render một cảnh Blender nặng hay build cả solution lớn, bạn thường chỉ biết máy có vấn đề **sau khi** nó đã treo. **Taskbar Hardware Monitor** đặt các chỉ số quan trọng nhất ngay trên thanh Taskbar để bạn nhìn thấy chúng mọi lúc, không phải mở Task Manager hay một ứng dụng giám sát cồng kềnh.

### Dành cho ai

- Họa sĩ 3D và dựng hình (Blender, Unreal Engine, Maya...) cần canh **VRAM** để không bị Out-of-Memory giữa chừng.
- Lập trình viên hay build, chạy test, mở nhiều IDE và trình duyệt, muốn biết thứ gì đang ăn RAM.
- Bất kỳ ai muốn một đồng hồ phần cứng nhỏ gọn, đẹp và không tốn tài nguyên.

### Hiển thị những gì

- **CPU**: phần trăm tải đa nhân và nhiệt độ package.
- **RAM**: đã dùng trên tổng (GB) và phần trăm.
- **GPU NVIDIA**: tải engine 3D/CUDA và nhiệt độ.
- **VRAM**: đã dùng trên tổng (GB), chỉ số sống còn khi render.
- **Ổ đĩa**: tốc độ đọc/ghi theo MB/s.

Màu sắc đổi theo mức độ: trắng khi bình thường, vàng từ 75%, đỏ từ 90% (hoặc nhiệt độ trên 82°C), nên chỉ cần liếc qua là biết.

### Điều khiển

- **Kéo thả** widget tới bất kỳ đâu trên Taskbar hay màn hình, hoặc chọn *Snap to Taskbar* để tự gắn cạnh vùng thông báo.
- **Bấm chuột trái** để mở thẻ chi tiết, kèm danh sách tiến trình dùng nhiều RAM nhất.
- **Bấm chuột phải** để mở menu: khóa vị trí, chọn chỉ số hiển thị, độ trong suốt (60-100%), tần suất làm mới (0,5/1/2 giây), tự chạy cùng Windows, chạy bằng quyền Administrator.
- Thu nhỏ xuống khay hệ thống bất cứ lúc nào.

### Nhẹ và tử tế

Viết bằng C# .NET (WPF), chỉ chiếm khoảng 30-50 MB RAM và dưới 0,1% CPU. Bản tải về là **portable**: giải nén là chạy, không cần cài đặt. Ứng dụng không thu thập dữ liệu cá nhân của bạn. Số liệu phần cứng đọc nhờ thư viện mã nguồn mở LibreHardwareMonitor.

### Cách dùng nhanh

1. Tải file ZIP, giải nén vào thư mục bất kỳ.
2. Chạy `TaskbarMonitor.exe` (hoặc `Start-Monitor.bat`).
3. Chuột phải vào widget và chọn **Start with Windows** nếu muốn tự chạy khi bật máy.
