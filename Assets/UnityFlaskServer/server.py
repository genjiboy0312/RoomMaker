from flask import Flask, request, jsonify
import cv2
import numpy as np
import base64

app = Flask(__name__)

@app.route('/analyze', methods=['POST'])
def analyze_image():
    print("Request received")  # 요청 들어오는지 확인
    data = request.json
    img_base64 = data['image']
    img_bytes = base64.b64decode(img_base64)
    nparr = np.frombuffer(img_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

    # -------------------
    # OpenCV 분석: 벽 좌표 추출
    # -------------------
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    _, thresh = cv2.threshold(gray, 50, 255, cv2.THRESH_BINARY_INV)  # 벽 영역 흑색 기준

    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    walls = []
    for cnt in contours:
        x, y, w, h = cv2.boundingRect(cnt)
        walls.append({"x": int(x), "y": int(y), "width": int(w), "height": int(h)})

    return jsonify({"walls": walls})

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000)
