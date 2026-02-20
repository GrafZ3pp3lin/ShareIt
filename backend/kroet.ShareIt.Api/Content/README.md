# Content Management API Documentation

## Overview

This implementation provides a complete content management system using MongoDB for storing files and text content. The system supports:

- **File uploads** (any file type, any size)
- **Text content** creation
- **Smart storage**: Small files (<256KB) are embedded in MongoDB documents, large files use GridFS
- **RESTful API** with versioned endpoints

## Architecture

### Storage Strategy

- **Small files (<256KB)**: Stored as base64-encoded data directly in MongoDB documents
- **Large files (≥256KB)**: Stored in GridFS with references in MongoDB documents
- **Text content**: Stored directly in MongoDB documents

### Technologies

- **MongoDB.Driver**: Official MongoDB .NET driver
- **GridFS**: For large file storage
- **ASP.NET Core Minimal APIs**: For endpoint routing
- **System.Text.Json**: For polymorphic JSON serialization

## API Endpoints

### Base URL
`http://localhost:{port}/api/v1/content`

### 1. Upload File

**POST** `/upload`

Uploads a file to the system.

**Request:**
- Content-Type: `multipart/form-data`
- Body:
  - `file`: The file to upload (IFormFile)
  - `userId`: (Optional) GUID of the user uploading the file

**Response:** `201 Created`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "contentType": "File",
  "createdAt": "2025-12-24T10:30:00Z",
  "size": 1024000,
  "userId": "user-guid-here",
  "fileName": "document.pdf",
  "mimeType": "application/pdf",
  "downloadUrl": "http://localhost/api/v1/content/{id}/download"
}
```

**Example cURL:**
```bash
curl -X POST http://localhost:5000/api/v1/content/upload \
  -F "file=@document.pdf" \
  -F "userId=3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

### 2. Create Text Content

**POST** `/text`

Creates text content.

**Request:**
- Content-Type: `application/json`
```json
{
  "text": "This is my text content",
  "title": "My Note",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response:** `201 Created`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "contentType": "Text",
  "createdAt": "2025-12-24T10:30:00Z",
  "size": 23,
  "userId": "user-guid-here",
  "text": "This is my text content",
  "title": "My Note"
}
```

**Example cURL:**
```bash
curl -X POST http://localhost:5000/api/v1/content/text \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello World","title":"Greeting"}'
```

### 3. Get Content Metadata

**GET** `/{id}`

Retrieves metadata for a content item without downloading the data.

**Response:** `200 OK`

For files:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "contentType": "File",
  "createdAt": "2025-12-24T10:30:00Z",
  "size": 1024000,
  "userId": "user-guid-here",
  "fileName": "document.pdf",
  "mimeType": "application/pdf",
  "downloadUrl": "http://localhost/api/v1/content/{id}/download"
}
```

For text:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "contentType": "Text",
  "createdAt": "2025-12-24T10:30:00Z",
  "size": 23,
  "text": "This is my text content",
  "title": "My Note"
}
```

**Example cURL:**
```bash
curl http://localhost:5000/api/v1/content/3fa85f64-5717-4562-b3fc-2c963f66afa6
```

### 4. Download Content

**GET** `/{id}/download`

Downloads the actual content data as a file stream.

**Response:** `200 OK`
- Content-Type: Appropriate MIME type (e.g., `application/pdf`, `text/plain`)
- Content-Disposition: `attachment; filename="original-filename.ext"`
- Body: File stream

**Example cURL:**
```bash
curl http://localhost:5000/api/v1/content/3fa85f64-5717-4562-b3fc-2c963f66afa6/download \
  -o downloaded-file.pdf
```

## Configuration

### appsettings.json

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "ShareItDb"
  }
}
```

### Environment Variables

You can override settings using environment variables:
- `MongoDB__ConnectionString`
- `MongoDB__DatabaseName`

## Database Schema

### Collections

#### `contents` Collection

Stores all content metadata with polymorphic documents.

**File Content Document:**
```json
{
  "_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "_t": "File",
  "CreatedAt": "2025-12-24T10:30:00Z",
  "Size": 1024000,
  "UserId": "user-guid-here",
  "FileName": "document.pdf",
  "MimeType": "application/pdf",
  "GridFsId": "gridfs-file-id",
  "EmbeddedData": null
}
```

**Text Content Document:**
```json
{
  "_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "_t": "Text",
  "CreatedAt": "2025-12-24T10:30:00Z",
  "Size": 23,
  "UserId": "user-guid-here",
  "Text": "This is my text content",
  "Title": "My Note"
}
```

#### `fs.files` and `fs.chunks` Collections

GridFS collections automatically created for large file storage.

## Error Handling

All endpoints return standard Problem Details responses for errors:

**400 Bad Request:**
```json
{
  "title": "Invalid file",
  "detail": "File is empty",
  "status": 400
}
```

**404 Not Found:**
```json
{
  "title": "Content not found",
  "detail": "Content with ID {id} was not found",
  "status": 404
}
```

## Testing

### Prerequisites

1. MongoDB must be running on `mongodb://localhost:27017`
2. .NET 10 SDK installed

### Running the API

```bash
cd kroet.ShareIt.Api
dotnet run
```

The API will be available at `http://localhost:5000` (or the port specified in launchSettings.json).

### Sample Test Sequence

```bash
# 1. Upload a file
FILE_ID=$(curl -X POST http://localhost:5000/api/v1/content/upload \
  -F "file=@test.pdf" \
  -s | jq -r '.id')

# 2. Get metadata
curl http://localhost:5000/api/v1/content/$FILE_ID

# 3. Download the file
curl http://localhost:5000/api/v1/content/$FILE_ID/download -o downloaded.pdf

# 4. Create text content
TEXT_ID=$(curl -X POST http://localhost:5000/api/v1/content/text \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello World","title":"Test"}' \
  -s | jq -r '.id')

# 5. Get text metadata
curl http://localhost:5000/api/v1/content/$TEXT_ID
```

## Future Enhancements

1. **Authentication/Authorization**: Add permission-based access control
2. **Content Expiration**: Implement TTL for temporary content
3. **File Validation**: Add MIME type validation and size limits
4. **Checksums**: Add file integrity verification
5. **Search**: Implement full-text search for content
6. **Versioning**: Support content versioning
7. **Thumbnails**: Generate previews for images
8. **Virus Scanning**: Integrate antivirus scanning for uploads

