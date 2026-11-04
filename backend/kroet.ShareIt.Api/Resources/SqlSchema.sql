-- PostgreSQL schema for content packages and contents

-- Table for content packages. Nothing names a package: item names and MIME
-- types live in the client-sealed per-item `meta` column.
CREATE TABLE IF NOT EXISTS content_packages (
    id UUID PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL,
    user_id UUID,
    is_private BOOLEAN,
    max_download_count INT,
    current_download_count INT NOT NULL DEFAULT 0,
    expires_at TIMESTAMPTZ,
    unlock_verifier TEXT,
    CONSTRAINT chk_download_count CHECK (current_download_count >= 0),
    CONSTRAINT chk_max_download_count CHECK (max_download_count IS NULL OR max_download_count > 0)
);

-- Upgrade databases created before the redesign. The dropped columns carried
-- names (title/description, file_name/mime_type) that now live in `meta`.
ALTER TABLE content_packages ADD COLUMN IF NOT EXISTS is_private BOOLEAN;
ALTER TABLE content_packages ADD COLUMN IF NOT EXISTS unlock_verifier TEXT;
ALTER TABLE content_packages DROP COLUMN IF EXISTS title;
ALTER TABLE content_packages DROP COLUMN IF EXISTS description;

-- Indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_packages_user_id ON content_packages(user_id);
CREATE INDEX IF NOT EXISTS idx_packages_expires_at ON content_packages(expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_packages_download_limit ON content_packages(current_download_count, max_download_count)
    WHERE max_download_count IS NOT NULL;

-- Keyset pagination for the owner's links list: newest first.
CREATE INDEX IF NOT EXISTS idx_packages_user_created
    ON content_packages(user_id, created_at DESC, id DESC)
    WHERE user_id IS NOT NULL;

-- Index for cleanup queries (finding expired or limit-reached packages)
CREATE INDEX IF NOT EXISTS idx_packages_for_cleanup ON content_packages(expires_at, current_download_count, max_download_count);


-- Table for external content metadata
CREATE TABLE IF NOT EXISTS contents (
    id UUID PRIMARY KEY,
    package_id UUID NOT NULL,
    content_type VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    size BIGINT NOT NULL,
    user_id UUID,
    meta TEXT,
    url TEXT NOT NULL,
    FOREIGN KEY (package_id) REFERENCES content_packages(id) ON DELETE CASCADE,
    CONSTRAINT chk_content_size CHECK (size >= 0)
);

-- Upgrade databases created before `meta` existed.
ALTER TABLE contents ADD COLUMN IF NOT EXISTS meta TEXT;
ALTER TABLE contents DROP COLUMN IF EXISTS file_name;
ALTER TABLE contents DROP COLUMN IF EXISTS mime_type;

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
CREATE INDEX IF NOT EXISTS idx_download_tokens_token ON download_tokens(token) WHERE not is_used;
CREATE INDEX IF NOT EXISTS idx_download_tokens_expires_at ON download_tokens(expires_at);
CREATE INDEX IF NOT EXISTS idx_download_tokens_content_id ON download_tokens(content_id);
CREATE INDEX IF NOT EXISTS idx_download_tokens_cleanup ON download_tokens(expires_at, is_used, used_at);
