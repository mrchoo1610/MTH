Ổ C đầy là chuyện quen thuộc, nhưng các công cụ "dọn rác" thường khiến người dùng e ngại vì lo xóa nhầm. **Disk Cleaner** đi theo hướng ngược lại: nguyên tắc số 1 là **không bao giờ xóa nhầm**.

### Quét thử trước, xóa sau

Mọi thao tác dọn dẹp đều bắt đầu bằng **quét thử (dry-run)**: app cho bạn xem sẽ giải phóng bao nhiêu GB ở từng mục, mà chưa xóa gì cả. Chỉ khi bạn bấm **Dọn dẹp** và xác nhận thì file mới bị xóa. Số liệu lúc quét dùng đúng bộ lọc của lúc xóa nên khớp với kết quả thực tế.

### An toàn theo thiết kế

- **Whitelist**: chỉ dọn những nơi đã được liệt kê trong `rules.json` như thư mục `%TEMP%`, Windows Temp, cache Chrome/Edge, cache npm, NuGet, pip, Yarn, Gradle, crash dump, thumbnail cache, Thùng rác.
- **SafetyGuard**: lớp bảo vệ chặn tuyệt đối `Windows`, `Program Files`, `ProgramData`, Documents, Desktop, Pictures, OneDrive, gốc ổ đĩa và chính thư mục người dùng, kể cả khi một rule vô tình trỏ tới.
- Chỉ xóa nội dung **bên trong** thư mục của rule, **không đi theo junction/symlink**, bỏ qua file OneDrive chỉ có trên đám mây.
- File đang bị khóa hoặc thiếu quyền sẽ được bỏ qua và ghi log, app không bị sập.
- Mỗi lần dọn ghi một file log liệt kê từng file đã xóa.

### Bốn tab

| Tab | Việc nó làm |
|---|---|
| Tổng quan | Dung lượng các ổ cố định và nút **Quét nhanh** cho các rule mặc định |
| Dọn dẹp | Chọn rule, quét thử rồi dọn, có tiến trình, hủy và bảng kết quả |
| Phân tích | Cây thư mục theo dung lượng để tìm thủ phạm làm đầy ổ |
| File lớn | Tìm top file lớn nhất và chỉ chuyển vào Thùng rác (khôi phục được) |

### Rule tùy chỉnh

Có một thư mục cache chiếm vài chục GB mà không có sẵn trong danh sách (CapCut, trình dựng video, game launcher...)? Mở tab Phân tích, chọn thư mục rồi bấm **Thêm vào Dọn dẹp**. Chính sách an toàn riêng sẽ từ chối các nơi nguy hiểm như `AppData`, `Downloads` hay thư mục chứa chương trình, và cảnh báo nếu tên thư mục không giống cache.

### Quyền Administrator chỉ khi cần

App chạy quyền thường. Chỉ với vài rule hệ thống (Windows Temp, cache Windows Update) app mới xin quyền UAC, và tiến trình nâng quyền chỉ nhận danh sách rule có sẵn, không tin đường dẫn nào từ bên ngoài.

### Cách dùng

1. Tải file ZIP và giải nén.
2. Chạy `DiskCleaner.exe`. Không cần cài đặt.
3. Vào **Tổng quan**, bấm **Quét nhanh**, rồi sang **Dọn dẹp** để xem chi tiết và xác nhận.
