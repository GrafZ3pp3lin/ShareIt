-- PostgreSQL schema for content packages and contents

-- Table for content packages
CREATE TABLE IF NOT EXISTS content_packages (
    id UUID PRIMARY KEY,
    title VARCHAR(500),
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    user_id UUID,
    max_download_count INT,
    current_download_count INT NOT NULL DEFAULT 0,
    expires_at TIMESTAMPTZ,
    CONSTRAINT chk_download_count CHECK (current_download_count >= 0),
    CONSTRAINT chk_max_download_count CHECK (max_download_count IS NULL OR max_download_count > 0)
);

-- Indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_packages_user_id ON content_packages(user_id);
CREATE INDEX IF NOT EXISTS idx_packages_expires_at ON content_packages(expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_packages_download_limit ON content_packages(current_download_count, max_download_count)
    WHERE max_download_count IS NOT NULL;

-- Index for cleanup queries (finding expired or limit-reached packages)
CREATE INDEX IF NOT EXISTS idx_packages_for_cleanup ON content_packages(id)
    WHERE (max_download_count IS NOT NULL AND current_download_count >= max_download_count)
        OR (expires_at IS NOT NULL AND expires_at < NOW());


-- Table for external content metadata
CREATE TABLE IF NOT EXISTS contents (
    id UUID PRIMARY KEY,
    package_id UUID NOT NULL,
    content_type VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    size BIGINT NOT NULL,
    user_id UUID,
    mime_type VARCHAR(255) NOT NULL,
    file_name VARCHAR(500) NOT NULL,
    url TEXT NOT NULL,
    FOREIGN KEY (package_id) REFERENCES content_packages(id) ON DELETE CASCADE,
    CONSTRAINT chk_content_size CHECK (size >= 0)
);

-- Indexes for contents
CREATE INDEX IF NOT EXISTS idx_contents_user_id ON contents(user_id);
CREATE INDEX IF NOT EXISTS idx_contents_created_at ON contents(created_at);
CREATE INDEX IF NOT EXISTS idx_contents_content_type ON contents(content_type);
CREATE INDEX IF NOT EXISTS idx_contents_package_id ON contents(package_id);


-- Table for download tokens
CREATE TABLE IF NOT EXISTS download_tokens (
    id UUID PRIMARY KEY,
    content_id UUID NOT NULL,
    token VARCHAR(64) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    client_id UUID NOT NULL,
    is_used BOOLEAN NOT NULL DEFAULT false,
    used_at TIMESTAMPTZ,
    CONSTRAINT uq_download_token UNIQUE (token)
);

-- Indexes for download tokens
CREATE INDEX IF NOT EXISTS idx_download_tokens_token ON download_tokens(token) WHERE NOT is_used;
CREATE INDEX IF NOT EXISTS idx_download_tokens_expires_at ON download_tokens(expires_at);
CREATE INDEX IF NOT EXISTS idx_download_tokens_content_id ON download_tokens(content_id);
CREATE INDEX IF NOT EXISTS idx_download_tokens_cleanup ON download_tokens(id) 
    WHERE expires_at < NOW() OR (is_used = true);

