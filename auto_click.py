import pyautogui
import threading
import keyboard
import time

pyautogui.FAILSAFE = True

clicking = False
click_count = 0

def click_loop():
    global clicking, click_count
    while True:
        if clicking:
            pyautogui.click()
            click_count += 1
            print(f"🖱️ Click #{click_count} tại {pyautogui.position()}")
            time.sleep(1)
        else:
            time.sleep(0.1)

def toggle(event=None):
    global clicking
    clicking = not clicking
    if clicking:
        print(f"\n▶️  BẬT auto click! (nhấn 9 để tắt)")
    else:
        print(f"\n⏸️  TẮT auto click! Tổng: {click_count} clicks (nhấn 9 để bật lại)")

print("🖱️ Auto Clicker")
print("=" * 40)
print("Nhấn phím [9] để BẬT / TẮT")
print("Nhấn Ctrl+C để thoát hoàn toàn")
print("=" * 40)

# Chạy click loop trong thread riêng
t = threading.Thread(target=click_loop, daemon=True)
t.start()

# Lắng nghe phím 9
keyboard.on_press_key("9", toggle)

try:
    keyboard.wait("esc")  # Nhấn Esc để thoát hoàn toàn
except KeyboardInterrupt:
    pass

print(f"\n✅ Đã thoát! Tổng: {click_count} clicks")
