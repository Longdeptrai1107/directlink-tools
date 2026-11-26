# DirectLink Share

Ứng dụng Windows x64, .NET Framework 4.7.2+, chia sẻ **đúng một file được chọn** bằng Cloudflare Quick Tunnel. Ứng dụng độc lập với DirectLink Downloader.

## Dùng bản EXE

1. Chạy `DirectLinkShare.exe`.
2. Bấm **Chọn file** hoặc kéo thả một file vào khung chọn.
3. Bấm **Tạo link công khai**. Đây là thao tác bắt đầu phục vụ file cho bất kỳ ai có link.
4. Khi trạng thái **Đang chia sẻ · HTTPS** xuất hiện, bấm **Sao chép**. Link trả nội dung file trực tiếp, dùng được trong trình duyệt hoặc DirectLink Downloader.
5. Giữ máy tính, mạng và ứng dụng hoạt động. Bấm **Dừng chia sẻ** hoặc đóng ứng dụng để ngừng phục vụ file và đóng tunnel.

Không cần tài khoản Cloudflare, tên miền, mở cổng router hoặc cài cloudflared riêng. Mỗi phiên tạo hostname mới. Đây không phải kho lưu trữ: file vẫn ở trên máy của bạn, được truyền qua tunnel khi có người yêu cầu. Tên file xuất hiện trong link và header tải xuống.

## Một file để mang theo

Chỉ cần EXE phát hành. Bản cloudflared Windows amd64 **2026.11.1** chính thức được nén, nhúng cùng giấy phép vào EXE. Khi tạo phiên, ứng dụng giải nén bản đã nhúng vào thư mục tạm riêng và kiểm tra SHA-256 trước khi chạy. Helper được dọn khi dừng bình thường; nếu app bị kết thúc cưỡng bức có thể còn file tạm trong `%TEMP%\DirectLinkShare`.

SHA-256 cloudflared gốc: `341deda3db5ec498a0482254d6c38c0b7cf01a30c37bb5dd95d2216d08955e35`.

Cloudflared được gắn với Windows Job Object `KILL_ON_JOB_CLOSE`, để hệ điều hành đóng tiến trình con nếu app thoát hoặc bị dừng. Không cài dịch vụ nền, không tự khởi động cùng Windows, không dùng thông tin đăng nhập tunnel có sẵn của người dùng. Giao diện **Hướng dẫn → Giấy phép cloudflared** chứa giấy phép Apache 2.0 của thành phần nhúng.

## HTTP và khả năng tải nhiều luồng

- Máy chủ TCP chỉ bind `127.0.0.1` trên một cổng động, không bind mạng LAN.
- Chỉ trả file được chọn trên đường dẫn chứa token ngẫu nhiên 256 bit. Không có directory listing, upload, xóa file hay ánh xạ đường dẫn tùy ý sang ổ đĩa.
- Hỗ trợ `GET`, `HEAD`, `Accept-Ranges: bytes`, byte range đóng/mở/suffix, `206`, `416`, `ETag`, `If-Range` và tên file UTF-8 trong `Content-Disposition`.
- Tối đa 64 kết nối cục bộ đang xử lý; không tự giới hạn băng thông ứng dụng. Truyền bất đồng bộ bằng buffer 256 KiB mỗi kết nối, không nạp cả file vào RAM.
- Giữ file ở chế độ chỉ đọc trong phiên, tránh các phần tải lấy từ phiên bản file khác nhau.
- Link chỉ được bật sao chép sau khi tunnel đã kết nối và ứng dụng kiểm tra HEAD trên chính URL HTTPS công khai thành công.
- Số liệu là byte thực tế gửi vào socket, số kết nối truyền dữ liệu và số yêu cầu GET hoàn tất. Các phần tải của cùng một file được tính là nhiều yêu cầu; không coi đây là số người tải.
- Dừng chia sẻ đóng origin và tunnel, nhưng không thu hồi được những byte người nhận đã nhận hoặc lưu trước đó.

## Giới hạn thực tế

Tốc độ phụ thuộc tốc độ **upload** của máy chia sẻ, mạng người tải, tuyến mạng và Cloudflare. Không thể cam kết “max speed”. Tải nhiều luồng có thể giúp nhưng không vượt được băng thông đường truyền.

Cloudflare mô tả Quick Tunnel là công cụ thử nghiệm/phát triển, không đảm bảo uptime, có giới hạn 200 yêu cầu đang xử lý và hostname thay đổi mỗi phiên. Với chia sẻ ổn định, tên miền cố định hoặc nhu cầu lâu dài, cần triển khai tunnel có quản lý/hosting phù hợp. App hiện dùng Quick Tunnel với kết nối HTTP/2 ra Cloudflare qua TCP 7844. Nếu mạng chặn cổng này, tunnel có thể không hoạt động; xem **Nhật ký** để biết lỗi.

Máy ngủ, mất mạng, đóng app, sự cố dịch vụ hoặc chính sách Cloudflare đều có thể làm gián đoạn link. EXE ứng dụng chưa ký số; bản cloudflared nhúng được tải từ bản phát hành chính thức và kiểm tra hash.

## Nguồn chính thức

- Quick Tunnel: https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/
- Bản cloudflared được nhúng và checksum: https://github.com/cloudflare/cloudflared/releases/tag/2026.11.1
- Giấy phép: https://github.com/cloudflare/cloudflared/blob/2026.11.1/LICENSE

## Build và kiểm thử

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Share.ps1
powershell -ExecutionPolicy Bypass -File .\Test-Share.ps1
powershell -ExecutionPolicy Bypass -File .\Test-Share-UI.ps1
powershell -ExecutionPolicy Bypass -File .\Test-Share.ps1 -Internet
powershell -STA -ExecutionPolicy Bypass -File .\UiSmoke-Share.ps1
```

Build xuất `release-share\DirectLinkShare.exe`. Bài kiểm thử Internet chỉ chia sẻ file dữ liệu ngẫu nhiên được sinh riêng, tải lại bằng 8 luồng, đối chiếu SHA-256 rồi dừng tunnel. Không dùng file riêng tư của người dùng làm dữ liệu kiểm thử.
