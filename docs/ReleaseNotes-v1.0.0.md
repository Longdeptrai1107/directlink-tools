DirectLink Tools gồm hai ứng dụng portable có giao diện tiếng Việt:

- **DirectLink Downloader 2.0:** tải link HTTP/HTTPS bằng 1–32 luồng, tự lưu vào Downloads, tự đổi tên khi trùng, hiển thị tốc độ, tiến độ và biểu đồ.
- **DirectLink Share 1.0:** phục vụ đúng một file được chọn qua localhost và Cloudflare Quick Tunnel; hỗ trợ HTTP Range cho tải nhiều luồng. cloudflared được nhúng trong EXE.

Tải EXE tương ứng, không cần tải mã nguồn. Dùng `SHA256SUMS.txt` để kiểm tra file phát hành. Yêu cầu .NET Framework 4.7.2 trở lên; Share yêu cầu Windows 64 bit. Các EXE ứng dụng chưa ký số.

**Điều kiện của Share:** giữ máy và ứng dụng hoạt động; ai có link đều có thể tải file. Link thay đổi mỗi phiên. Mạng cần cho phép kết nối ra Cloudflare qua TCP 7844. Trong kiểm thử tại máy phát triển, API đã cấp hostname nhưng tunnel không kết nối được qua cổng này, nên tải công khai từ đầu đến cuối chưa được xác minh thành công. Ứng dụng kiểm tra URL công khai trước khi bật sao chép link.

Bộ kiểm thử cục bộ kiểm tra tải 8 luồng với SHA-256 khớp, Range, lỗi kết nối, hủy, tên file, tránh ghi đè và dừng chia sẻ. Tốc độ thực tế phụ thuộc đường truyền và máy chủ; Share còn phụ thuộc tốc độ upload của máy chia sẻ.
