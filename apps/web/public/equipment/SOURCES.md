# Ảnh thiết bị

Ảnh trong thư mục này được build cùng web và phục vụ tại `/equipment/<tên-tệp>`. Ảnh không phụ thuộc trang ngoài, nên chạy được trên máy của mọi thành viên và trong Docker.

## Cách thêm ảnh

1. **Đặt tên tệp** theo `demoCode` của thiết bị trong `data/demo/equipment-catalog.vi.json`, viết thường. Ví dụ `"demoCode": "UNO"` thì đặt tên `uno.webp`.
   - Chỉ dùng `a-z`, `0-9`, `-`.
   - Đuôi tệp: `.webp` (nên dùng), `.png`, `.jpg` hoặc `.jpeg`.
2. **Kích thước:** cạnh dài khoảng 800px, dưới 150 KB. Nên dùng nền trắng hoặc trong suốt, thiết bị nằm giữa ảnh.
3. **Ghi đường dẫn:** sửa `"imageUrl": null` thành `"imageUrl": "/equipment/uno.webp"` trong tệp JSON.
4. **Ghi nguồn** vào bảng bên dưới.
5. **Nạp vào cơ sở dữ liệu:**
   - Chạy `./scripts/Import-EquipmentCatalog.ps1 -ValidateOnly`. Script báo lỗi nếu tên tệp sai quy tắc hoặc tệp ảnh không có.
   - Sau đó chạy `-Apply -UpdateExisting`.
   - Nhân viên cũng có thể nhập đường dẫn trong ô "URL hình ảnh" ở trang quản lý danh mục.

Nếu ảnh lỗi hoặc thiếu, giao diện hiện chữ cái đầu của danh mục thay cho ảnh.

## Nguồn ảnh được phép dùng

- Ảnh tự chụp thiết bị của lab. Đây là lựa chọn tốt nhất vì đúng mẫu mã.
- Ảnh có giấy phép mở, ví dụ Wikimedia Commons (CC BY, CC BY-SA, CC0, public domain). Phải ghi tác giả và giấy phép.
- **Không** lấy ảnh từ trang bán hàng hoặc trang của hãng khi chưa được phép.

## Bảng nguồn

| Tệp | Thiết bị | Nguồn / tác giả | Giấy phép |
|---|---|---|---|
