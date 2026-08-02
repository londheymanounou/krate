#![cfg(target_os = "android")]

use crate::{currency, i18n, system, tools};
use jni::JNIEnv;
use jni::objects::{JClass, JObject, JString};
use jni::sys::{jint, jobject, jstring};

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_run<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    id: JString<'local>,
    input: JString<'local>,
) -> jobject {
    let id_str: String = env.get_string(&id).unwrap_or_default().into();
    let input_str: String = env.get_string(&input).unwrap_or_default().into();

    let (ok, text) = match tools::run(&id_str, &input_str) {
        Ok(t) => (1_i32, t),
        Err(t) => (0_i32, t),
    };

    let text_jstring = env.new_string(text).unwrap_or_else(|_| env.new_string("").unwrap());
    
    let krate_result_class = env.find_class("com/krate/KrateResult").unwrap();
    let result = env.new_object(
        krate_result_class,
        "(ILjava/lang/String;)V",
        &[jni::objects::JValue::Int(ok), jni::objects::JValue::Object(&text_jstring)],
    ).unwrap();

    result.into_raw()
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_setLanguage<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    tag: JString<'local>,
) {
    if let Ok(tag_str) = env.get_string(&tag) {
        i18n::set_language(tag_str.to_str().unwrap());
    }
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_setRuntime<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    text: JString<'local>,
) {
    if let Ok(text_str) = env.get_string(&text) {
        system::set_runtime(text_str.to_str().unwrap());
    }
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_toolCount<'local>(
    _env: JNIEnv<'local>,
    _class: JClass<'local>,
) -> jint {
    tools::catalog().len() as jint
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_toolId<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    index: jint,
) -> jstring {
    let idx = index as usize;
    if let Some(tool) = tools::catalog().get(idx) {
        let jstr = env.new_string(tool.id).unwrap();
        jstr.into_raw()
    } else {
        JObject::null().into_raw() as jstring
    }
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_toolName<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    index: jint,
) -> jstring {
    let idx = index as usize;
    if let Some(tool) = tools::catalog().get(idx) {
        let jstr = env.new_string(tool.name()).unwrap();
        jstr.into_raw()
    } else {
        JObject::null().into_raw() as jstring
    }
}

#[no_mangle]
pub extern "system" fn Java_com_krate_Core_currencyStoreRates<'local>(
    mut env: JNIEnv<'local>,
    _class: JClass<'local>,
    base: JString<'local>,
    json: JString<'local>,
) -> jint {
    let base_str: String = env.get_string(&base).unwrap_or_default().into();
    let json_str: String = env.get_string(&json).unwrap_or_default().into();
    match currency::store_rates(&base_str, &json_str) {
        Ok(_) => 1,
        Err(_) => 0,
    }
}
