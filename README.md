# VirtuHire - AI Interview Simulator

Ứng dụng mô phỏng phỏng vấn bằng VR/Mobile kết hợp AI.

## Thành phần chính

- `Assets/`: phần Unity, scene, UI, audio và script điều khiển.
- `.AI/AI/`: backend FastAPI xử lý STT, hội thoại AI và TTS.

## Chạy backend

Yêu cầu:
- Python 3.10 trở lên
- FFmpeg đã được cài và thêm vào `PATH`

Cài đặt:

```bash
python -m venv .venv
.venv\Scripts\activate
cd .AI/AI
pip install -r requirements.txt
```

Tạo file `.env` trong `.AI/AI/` nếu cần:

```env
GEMINI_API_KEY=your_api_key
```

Chạy server:

```bash
python main.py
```

Mặc định backend chạy ở `http://127.0.0.1:8000`.

## Mở project Unity

1. Mở thư mục `InterviewSimulator/` bằng Unity Hub.
2. Dùng Unity 2022 LTS hoặc mới hơn nếu có thể.
3. Mở scene chính để demo.
4. Kiểm tra `AIAudioClient` trong Inspector:
   - Chạy trên máy tính: `http://127.0.0.1:8000`
   - Chạy trên điện thoại: đổi sang IP LAN của máy chạy backend

## Chạy thử

1. Khởi động backend trước.
2. Bấm `Play` trong Unity.
3. Thử các flow chính:
   - chọn ngôn ngữ
   - chọn vị trí phỏng vấn
   - ghi âm / gửi câu hỏi
   - nhận phản hồi giọng nói từ AI

## Build Android

1. Vào `File > Build Settings`
2. Chọn `Android`
3. Bấm `Switch Platform`
4. Kiểm tra Android SDK/NDK/OpenJDK trong Unity Hub
5. Dùng `Build And Run` để cài lên thiết bị

## Ghi chú

- Nếu app chạy trên điện thoại còn backend chạy trên máy tính, hai thiết bị phải cùng mạng LAN.
- Một số asset sample vẫn được giữ lại vì scene hiện tại còn tham chiếu trực tiếp.
