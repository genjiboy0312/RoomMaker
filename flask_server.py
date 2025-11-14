from flask import Flask, request, jsonify
import uuid
from datetime import datetime

app = Flask(__name__)

@app.route("/upload_project", methods=["POST"])
def upload_project():
    # Unity에서 보낸 JSON
    data = request.get_json()
    print("Received data from Unity:", data)

    # 샘플 ProjectData 생성
    project_data = {
        "projectId": str(uuid.uuid4()),
        "lastUpdated": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "buildings": [
            {
                "id": "b_1",
                "name": "empty_building",
                "position": {"x":0, "y":0, "z":0},
                "floors": [
                    {
                        "id": "f_1",
                        "name": "1층",
                        "height": 3.0,
                        "walls": [
                            {
                                "id": "w_1",
                                "name": "wall_1",
                                "properties": {
                                    "startPoint": {"x":0, "y":0, "z":0},
                                    "endPoint": {"x":10, "y":0, "z":0},
                                    "thickness": 0.25
                                },
                                "children": [
                                    {
                                        "id": "door_001",
                                        "name": "door_1",
                                        "type": "door",
                                        "properties": {
                                            "offset": {"x":1.0, "y":0.0, "z":0.0},
                                            "width": 1.2,
                                            "height": 2.1
                                        }
                                    }
                                ]
                            },
                            {
                                "id": "w_2",
                                "name": "wall_2",
                                "properties": {
                                    "startPoint": {"x":10, "y":0, "z":0},
                                    "endPoint": {"x":10, "y":0, "z":10},
                                    "thickness": 0.25
                                },
                                "children": []
                            }
                        ]
                    }
                ]
            }
        ]
    }

    return jsonify(project_data)


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, debug=True)
