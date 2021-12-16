# Single Request

```
effectiveStartTime/planId/dimension/quantity/resourceId
```

# Single Response

## 200 OK

```
status/usageEventId/messageTime
effectiveStartTime/planId/dimension/quantity/resourceId
```

## 400 Bad request

```
message/code
details[]
    message/code/target
```

## 403 Forbidden

## 409 Conflict

```
message/code
additionalInfo
    acceptedMessage
        status/usageEventId/messageTime
        effectiveStartTime/planId/dimension/quantity/resourceId
```

# Batch Responses

## Success

```
status/usageEventId/messageTime
effectiveStartTime/planId/dimension/quantity/resourceId
```

## Error

```
status
effectiveStartTime/planId/dimension/quantity/resourceId
error
    message/code
    additionalInfo
        acceptedMessage
            status/usageEventId/messageTime
            effectiveStartTime/planId/dimension/quantity/resourceId
```
