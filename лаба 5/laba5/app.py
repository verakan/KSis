import os  # Для работы с файловой системой
import shutil  # Для операций с файлами и папками (удаление и т.д.)
from datetime import datetime  # Для работы с датами и временем
from flask import Flask, request, send_from_directory, jsonify, abort, render_template
from werkzeug.utils import secure_filename

# Создание экземпляра Flask-приложения
app = Flask(__name__)

# Определение корневой папки для хранения файлов
STORAGE_ROOT = os.path.join(os.getcwd(), 'storage')
# Если папка не существует - создаем ее
if not os.path.exists(STORAGE_ROOT):
    os.makedirs(STORAGE_ROOT)


def get_abs_path(path):
    """
    Преобразует запрошенный путь в абсолютный путь в файловой системе,
    предотвращая обход за пределы STORAGE_ROOT.
    """
    # Нормализуем путь (убираем '..', '.', лишние слеши) и убираем ведущие разделители
    safe_path = os.path.normpath(path).lstrip(os.path.sep)
    # Объединяем с корневой папкой хранилища
    return os.path.join(STORAGE_ROOT, safe_path)


# Маршрут для главной страницы (GET-запрос)
@app.route('/', methods=['GET'])
def index():
    """
    Отдает HTML-интерфейс для работы через браузер.
    """
    return render_template('index.html')


# Основной маршрут для работы с файлами (поддерживает PUT, GET, HEAD, DELETE)
@app.route('/<path:subpath>', methods=['PUT', 'GET', 'HEAD', 'DELETE'])
def handle_file(subpath):
    # Получаем абсолютный безопасный путь
    abs_path = get_abs_path(subpath)

    # Обработка PUT-запроса (загрузка/обновление файла)
    if request.method == 'PUT':
        # Проверяем, существовал ли файл до этого
        existed = os.path.exists(abs_path)
        try:
            # Создаем все необходимые подкаталоги (если их нет)
            os.makedirs(os.path.dirname(abs_path), exist_ok=True)
            # Записываем данные файла (request.data содержит тело запроса)
            with open(abs_path, 'wb') as f:
                f.write(request.data)
            # Возвращаем 201 если файл создан, 200 если обновлен
            return ('', 201) if not existed else ('', 200)
        except Exception as e:
            # В случае ошибки возвращаем 500
            return str(e), 500

    # Обработка GET-запроса (получение файла или списка файлов в каталоге)
    elif request.method == 'GET':
        if os.path.exists(abs_path):
            # Если это каталог
            if os.path.isdir(abs_path):
                # Получаем список файлов в каталоге
                items = os.listdir(abs_path)
                response_list = []
                # Формируем информацию о каждом файле/каталоге
                for item in items:
                    item_path = os.path.join(abs_path, item)
                    response_list.append({
                        'name': item,
                        'type': 'directory' if os.path.isdir(item_path) else 'file'
                    })
                # Если клиент принимает HTML, формируем HTML-страницу
                if 'text/html' in request.headers.get('Accept', ''):
                    html = f'<html><head><title>Содержимое каталога: {subpath}</title></head><body>'
                    html += f'<h1>Содержимое каталога: {subpath}</h1><ul>'
                    for entry in response_list:
                        html += f"<li>{entry['name']} ({entry['type']})</li>"
                    html += '</ul></body></html>'
                    return html, 200, {'Content-Type': 'text/html'}
                else:
                    # Иначе возвращаем JSON
                    return jsonify(response_list), 200
            else:
                # Если это файл - отправляем его клиенту
                return send_from_directory(os.path.dirname(abs_path), os.path.basename(abs_path))
        else:
            # Если файл/каталог не найден - 404
            abort(404)

    # Обработка HEAD-запроса (получение метаданных файла)
    elif request.method == 'HEAD':
        if os.path.isfile(abs_path):
            # Получаем информацию о файле
            stat = os.stat(abs_path)
            # Формируем заголовки
            headers = {
                'Content-Length': str(stat.st_size),
                'Last-Modified': datetime.fromtimestamp(stat.st_mtime).strftime('%a, %d %b %Y %H:%M:%S')
            }
            # Читаем содержимое файла (хотя для HEAD это не обязательно)
            with open(abs_path, 'r', encoding='utf-8') as file:
                content = file.read()

            # Создаем ответ и убираем ненужные заголовки
            response = app.make_response((content, 200, headers))
            response.headers.remove('Server')
            response.headers.remove('Date')
            response.headers.remove('Content-Type')
            response.headers.remove('Connection')
            return response
        else:
            abort(404)

    # Обработка DELETE-запроса (удаление файла/каталога)
    elif request.method == 'DELETE':
        if os.path.exists(abs_path):
            try:
                # Удаляем каталог или файл
                if os.path.isdir(abs_path):
                    shutil.rmtree(abs_path)
                else:
                    os.remove(abs_path)
                # Успешное удаление - 204 (No Content)
                return '', 204
            except Exception as e:
                return str(e), 500
        else:
            abort(404)
    else:
        # Если метод не поддерживается - 405
        abort(405)


# Специальный маршрут для скачивания файлов
@app.route('/download/<path:subpath>', methods=['GET'])
def download_file(subpath):
    abs_path = get_abs_path(subpath)
    if os.path.exists(abs_path) and os.path.isfile(abs_path):
        # Отправляем файл как вложение (as_attachment=True)
        response = send_from_directory(os.path.dirname(abs_path), os.path.basename(abs_path), as_attachment=True)
        # Устанавливаем заголовок Content-Length
        file_stats = os.stat(abs_path)
        response.headers['Content-Length'] = str(file_stats.st_size)
        return response
    else:
        abort(404)


if __name__ == '__main__':
    app.run(debug=True, host='0.0.0.0')