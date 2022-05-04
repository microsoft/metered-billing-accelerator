#/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

AZURE_SAAS_DEMO_SETTINGS="${basedir}/settings.json"

if [[ ! -f "${AZURE_SAAS_DEMO_SETTINGS}" ]]; then
    echo "{}" > "${AZURE_SAAS_DEMO_SETTINGS}" ; 
fi

function get-value { 
    local key="$1" ;
    local json ;
    json="$( cat "${AZURE_SAAS_DEMO_SETTINGS}" )" ;
    echo "${json}" | jq -r ".${key}"
}

echo "$( get-value "$1" "$2" )"
