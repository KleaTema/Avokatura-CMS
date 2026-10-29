USE ash_core;

-- Task 015: managing offices (with their Kryetar) and institutions gets its own system permission,
-- separate from admin.users and admin.lookups. Granted to ADMIN only.
INSERT INTO permissions (code, scope, description) VALUES
 ('admin.org', 'SYSTEM', 'Menaxhon zyrat, Kryetarët dhe institucionet');

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.code = 'admin.org'
WHERE r.code = 'ADMIN';
