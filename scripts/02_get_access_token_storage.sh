#!/bin/bash

#
# Fetch an access token for Azure Storage.
#
function get_access_token_storage {
  curl \
      --silent \
      --request POST \
      --url "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" \
      --data-urlencode "response_type=token" \
      --data-urlencode "grant_type=client_credentials" \
      --data-urlencode "scope=https://storage.azure.com/.default" \
      --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
      --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
      | jq -r '.access_token'
}

get_access_token_storage
