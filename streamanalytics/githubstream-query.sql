WITH Payload AS (
    SELECT
        payload,
        [type],
        created_at
    FROM
        githubstream 
    WHERE
        [public] = 1
)

SELECT
  *
INTO
  comments
FROM
  Payload
WHERE
    type like '%Comment%'

SELECT
  *
INTO
  pushes
FROM
  Payload
WHERE
    type = 'PushEvent'