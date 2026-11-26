# DirectLink Downloader · v2

Bản portable: chỉ cần **một file `DirectLinkDownloader.exe`**. Không cần file cấu hình, DLL, bộ icon, trình cài đặt hoặc kết nối Internet để nạp giao diện. Sử dụng .NET Framework 4.7.2 trở lên đã cài trên máy Windows.

## Sử dụng

1. Mở EXE, dán link HTTP/HTTPS trực tiếp đến file.
2. Bỏ trống tên file để tự lấy từ máy chủ hoặc nhập tên tùy chọn.
3. Chọn 1–32 luồng; mặc định 8.
4. Bấm **Bắt đầu tải**. File tự lưu vào thư mục **Downloads** của tài khoản Windows hiện tại, kể cả khi thư mục đã được chuyển vị trí.
5. Nếu trùng tên, ứng dụng tự thêm `(1)`, `(2)`...; không ghi đè file cũ.

Nút **Dán**, **Hủy tải**, **Mở Downloads** và **Hướng dẫn** hoạt động trực tiếp trong ứng dụng. Dùng Ctrl+L để chọn ô link, Enter để bắt đầu. Thanh tiến độ, biểu đồ tốc độ, số kết nối và thời gian còn lại lấy từ lượt tải thực tế.

## Cơ chế tải và giới hạn

- Kiểm tra HTTP Range, tải nhiều phần bằng các luồng riêng, kiểm tra từng Content-Range và kích thước, ghép theo thứ tự.
- File nhỏ dùng ít kết nối hơn. Máy chủ bỏ qua Range được tải bằng một luồng.
- Tự thử lại tối đa ba lần cho mỗi phần khi lỗi kết nối. Mỗi lần thử lại tải lại phần đó từ đầu.
- Dùng ETag/Last-Modified khi được cung cấp để phát hiện file thay đổi. Kiểm tra kích thước không thay thế kiểm tra SHA-256 hoặc chữ ký nhà phát hành.
- File đích chỉ xuất hiện khi lượt tải đã hoàn tất. Có dọn file tạm khi hoàn tất, lỗi hoặc hủy bình thường. Tắt cưỡng bức hoặc mất điện có thể để lại thư mục `.directlink-*` trong Downloads; có thể xóa khi ứng dụng đã dừng.
- Cần dung lượng trống khoảng hai lần kích thước file khi ghép.
- Tốc độ phụ thuộc mạng, ổ đĩa và giới hạn máy chủ. Nhiều luồng không đảm bảo luôn nhanh hơn.
- Chưa có tiếp tục tải sau khi đóng, cookie đăng nhập, tải hàng loạt hoặc xử lý trang tải trung gian. Link trả HTML có thể được lưu thành HTML; cần dùng link trực tiếp thực sự.
- Không tự động chạy file đã tải. EXE chưa ký số.

## Biên dịch và kiểm thử

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
powershell -ExecutionPolicy Bypass -File .\Test.ps1
powershell -ExecutionPolicy Bypass -File .\Test-UI.ps1
```

Bản mới nằm ở `release\DirectLinkDownloader.exe`. Icon nhiều độ phân giải và manifest được nhúng vào EXE khi biên dịch. Không cần mang theo các file mã nguồn hoặc `.build`.

Các bài kiểm thử dùng máy chủ HTTP cục bộ để kiểm tra tải song song, hash file, fallback, thử lại, chuyển hướng, phản hồi sai, hủy, đặt tên từ header và tránh ghi đè. `UiSmoke.ps1` kiểm tra giao diện và xuất ảnh trong `test-output`.
