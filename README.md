# VirtuHire - AI Interview Simulator

Dự án mô phỏng phỏng vấn bằng công nghệ thực tế ảo (VR) và Mobile, sử dụng AI (FastAPI, Whisper STT, Piper TTS, Gemini LLM) để đóng vai trò người phỏng vấn tương tác trực tiếp bằng giọng nói với người học. 

---

## Cơ cấu thư mục chính

- **`/Assets/`**: Chứa toàn bộ tài nguyên Unity (Mã nguồn C#, Giao diện UI, file âm thanh, Scene, Component điều khiển Cardboard VR).
- **`/.AI/AI/`**: Chứa mã nguồn của Backend AI (FastAPI server xử lý logic hội thoại, nhận diện giọng nói và tổng hợp giọng nói của phiên phỏng vấn).

---

## 1. Hướng dẫn cài đặt và chạy Backend AI

### Yêu cầu cài đặt
- **Python 3.10** trở lên.
- Đã cài đặt [FFmpeg](https://ffmpeg.org/download.html) và thêm vào biến môi trường `PATH` của máy (bắt buộc để backend xử lý file âm thanh ghi âm từ Unity).

### Thiết lập môi trường

1. Mở Terminal (Powershell/Cmd) tại thư mục gốc của dự án (`InterviewSimulator/`).
2. Tạo môi trường ảo (nếu bạn chưa có):
   ```bash
   python -m venv .venv
   ```
3. Kích hoạt môi trường ảo:
   - **Windows:** `.venv\Scripts\activate`
   - **Mac/Linux:** `source .venv/bin/activate`
4. Di chuyển vào thư mục AI và cài đặt các thư viện cần thiết:
   ```bash
   cd .AI/AI
   pip install -r requirements.txt
   ```
5. **Cấu hình API Key:**
   Đảm bảo bạn có file `.env` ở trong thư mục `.AI/AI/` chứa các key cần dùng, ví dụ:
   ```env
   GEMINI_API_KEY=dien_api_key_cua_ban_vao_day
   ```
   *(Các model TTS và STT cục bộ như Piper/Whisper sẽ tự động được tải ở lần gọi đầu tiên vào thư mục `models/` nếu chưa có).*

### Chạy Server AI

Trong thư mục `.AI/AI/` với môi trường ảo đã được kích hoạt, bạn sử dụng lệnh:
```bash
python main.py
```
> **Mẹo:** Giao diện điều khiển (Admin Dashboard) để theo dõi đoạn chat và gửi lệnh ẩn có thể được truy cập tại: `http://127.0.0.1:8000/admin` trên trình duyệt máy tính.

*(Lưu ý: Nếu bạn Build Unity ra điện thoại mà muốn nó giao tiếp với máy tính, máy tính phải mở cửa mạng LAN. Khi đó hãy bắt đầu uvicorn bằng lệnh: `uvicorn main:app --host 0.0.0.0 --port 8000 --reload`)*

---

## 2. Hướng dẫn cài đặt và cấu hình Unity

### Yêu cầu hệ thống
- **Unity Editor** (phiên bản 2022 LTS trở lên được khuyên dùng, hỗ trợ module Android Build Support).
- Bộ SDK, NDK, OpenJDK (cài trực tiếp qua Unity Hub).

### Cấu trúc và Setup trên Editor
1. Mở **Unity Hub**, chọn **Add project** và điều hướng tới thư mục gốc `InterviewSimulator/`.
2. Mở dự án lên, vào thư mục `Assets/Scenes/` và mở Scene đóng vai trò là điểm bắt đầu.
3. **Cấu hình kết nối tới Server:**
   - Trong Hierarchy, tìm Game Object quản lý âm thanh (ví dụ như `AIAudioManager` - vật thể mà bạn gắn script `AIAudioClient.cs`).
   - Ở cửa sổ **Inspector**, tìm mục **Server Url**:
     - **Chạy Play Mode trên máy tính:** Để `http://127.0.0.1:8000`.
     - **Build ra điện thoại (Android/VR):** Đổi IP thành IP cục bộ của máy tính của bạn (VD: `http://192.168.1.123:8000`).

### Chạy Test trực tiếp (Play Mode)
1. Hãy chắc chắn Terminal chạy Backend AI (FastAPI) chưa bị tắt.
2. Bấm nút **Play** ở trên cùng Unity.
3. Test các tính năng bằng cách thao tác với UI (Gaze/Click rọi mắt vào nút): Bắt đầu ghi âm, Đổi ngôn ngữ Tiếng Việt/English, chọn loại phỏng vấn.
4. Mọi lỗi về kết nối sẽ đều được in ra ở bảng **Console** bên trong Unity.

### Build và Cài đặt lên Kính / Điện thoại Android

1. Đi tới **File > Build Settings**.
2. Đổi nền tảng biên dịch sang Mobile bằng cách chọn **Android**, rồi bấm **Switch Platform**.
3. Tại **Edit > Project Settings > XR Plug-in Management**, kiểm tra chắc chắn đã đánh dấu tích cấu hình cho **Cardboard** (nếu app xuất VR).
4. Cắm cáp kết nối từ điện thoại hệ điều hành Android vào máy tính, đảm bảo đã bật cấu hình lập trình viên (USB Debugging).
5. Nhấn **Build And Run** để Unity biên dịch project ra file `hr.apk` và thiết lập trực tiếp lên thiết bị di động của bạn để trải nghiệm.
