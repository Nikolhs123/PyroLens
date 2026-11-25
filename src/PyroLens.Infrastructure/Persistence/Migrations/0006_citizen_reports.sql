-- Reports from the public: "I can see smoke here". Anyone can send one from /public; dispatchers mark them seen or
-- dismiss them. No name, phone number or IP address is stored, only the place, what was seen and an optional note.
CREATE TABLE citizen_reports (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    location      geometry(Point, 4326) NOT NULL,
    kind          text NOT NULL CHECK (kind IN ('smoke', 'flames', 'other')),
    note          text CHECK (length(note) BETWEEN 1 AND 500),
    submitted_at  timestamptz NOT NULL DEFAULT now(),
    status        text NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'seen', 'dismissed')),
    reviewed_by   text CHECK (length(reviewed_by) <= 40),
    reviewed_at   timestamptz,
    -- A new report has no reviewer; a reviewed one has both who and when.
    CONSTRAINT ck_citizen_reports_review CHECK (
        (status = 'new' AND reviewed_by IS NULL AND reviewed_at IS NULL) OR
        (status <> 'new' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL))
);

-- "Reports within 3 km of this fire" (escalation rules) and "the fire nearest to this report".
CREATE INDEX ix_citizen_reports_location ON citizen_reports USING gist ((location::geography));
CREATE INDEX ix_citizen_reports_recent ON citizen_reports (submitted_at DESC);
