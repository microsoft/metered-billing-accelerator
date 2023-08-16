#!/bin/bash

basedir="/mnt/c/github/chgeuer/metered-billing-accelerator/scripts/Metering.SharedResourceBroker"
echo "Working in directory ${basedir}"

CONFIG_FILE="${basedir}/config.json"

function get-value { 
    local key="$1" ;
    local json ;
    
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" | jq -r "${key}"
}

resource="https://storage.azure.com/.default"
access_token="$(curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/$( get-value '.aad.tenantId')/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$( get-value '.creds.appAppId')" \
    --data-urlencode "client_secret=$( get-value '.creds.client_secret')" \
    --data-urlencode "scope=${resource}" \
    | jq -r '.access_token' )"

echo "${access_token}" | jq -R 'split(".") | .[1] | @base64d | fromjson'

# .creds.appObjectId
# .creds.appAppId
# .creds.spObjectId
# .creds.client_secret