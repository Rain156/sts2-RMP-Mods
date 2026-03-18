extends SceneTree

func find_mod_image_ctex(imported_dir: String) -> String:
	var dir := DirAccess.open(imported_dir)
	if dir == null:
		return ""
	for file in dir.get_files():
		if file.begins_with("mod_image.png-") and file.ends_with(".ctex"):
			return file
	return ""

func build_import_content(source_file: String, ctex_target: String) -> String:
	return "[remap]\n\nimporter=\"texture\"\ntype=\"CompressedTexture2D\"\npath=\"%s\"\nmetadata={\n\"vram_texture\": false\n}\n\n[deps]\n\nsource_file=\"%s\"\ndest_files=[\"%s\"]\n\n[params]\n\ncompress/mode=0\ncompress/high_quality=false\ncompress/lossy_quality=0.7\ncompress/uastc_level=0\ncompress/rdo_quality_loss=0.0\ncompress/hdr_compression=1\ncompress/normal_map=0\ncompress/channel_pack=0\nmipmaps/generate=false\nmipmaps/limit=-1\nroughness/mode=0\nroughness/src_normal=\"\"\nprocess/channel_remap/red=0\nprocess/channel_remap/green=1\nprocess/channel_remap/blue=2\nprocess/channel_remap/alpha=3\nprocess/fix_alpha_border=true\nprocess/premult_alpha=false\nprocess/normal_map_invert_y=false\nprocess/hdr_as_srgb=false\nprocess/hdr_clamp_exposure=false\nprocess/size_limit=0\ndetect_3d/compress_to=1\n" % [ctex_target, source_file, ctex_target]

func add_mod_image_import_chain(packer: PCKPacker, project_dir: String, image_target_path: String) -> int:
	var imported_dir := project_dir.path_join(".godot/imported")
	var ctex_name := find_mod_image_ctex(imported_dir)
	if ctex_name.is_empty():
		return OK
	var ctex_source := imported_dir.path_join(ctex_name)
	var ctex_target := "res://.godot/imported/%s" % ctex_name
	var add_ctex_ok := packer.add_file(ctex_target, ctex_source)
	if add_ctex_ok != OK:
		return add_ctex_ok
	var md5_name := "%s.md5" % ctex_name
	var md5_source := imported_dir.path_join(md5_name)
	if FileAccess.file_exists(md5_source):
		var add_md5_ok := packer.add_file("res://.godot/imported/%s" % md5_name, md5_source)
		if add_md5_ok != OK:
			return add_md5_ok
	var temp_import_local := "user://mod_image_runtime.import"
	var temp_import_global := ProjectSettings.globalize_path(temp_import_local)
	var file := FileAccess.open(temp_import_local, FileAccess.WRITE)
	if file == null:
		return ERR_CANT_CREATE
	file.store_string(build_import_content(image_target_path, ctex_target))
	file.close()
	var add_import_ok := packer.add_file("%s.import" % image_target_path, temp_import_global)
	if add_import_ok != OK:
		return add_import_ok
	DirAccess.remove_absolute(temp_import_global)
	return OK

func add_external_dir(packer: PCKPacker, source_dir: String, target_dir: String) -> int:
	var dir := DirAccess.open(source_dir)
	if dir == null:
		return OK
	var files := dir.get_files()
	for file in files:
		var source_file := source_dir.path_join(file)
		var target_file := target_dir.path_join(file)
		var add_file_ok := packer.add_file(target_file, source_file)
		if add_file_ok != OK:
			return add_file_ok
	var directories := dir.get_directories()
	for directory in directories:
		var recurse_ok := add_external_dir(packer, source_dir.path_join(directory), target_dir.path_join(directory))
		if recurse_ok != OK:
			return recurse_ok
	return OK

func _initialize():
	var output_dir := "res://build"
	var output_file := "res://build/RemoveMultiplayerPlayerLimit.pck"
	var manifest_path := "res://RemoveMultiplayerPlayerLimit.json"
	var project_dir := ProjectSettings.globalize_path("res://")
	var external_asset_dir := project_dir.path_join("RemoveMultiplayerPlayerLimit")
	DirAccess.make_dir_recursive_absolute(output_dir)
	var packer := PCKPacker.new()
	var ok := packer.pck_start(output_file)
	if ok != OK:
		push_error("pck_start failed: %s" % ok)
		quit(1)
		
	var files := PackedStringArray([
		manifest_path
	])
	
	for file in files:
		var add_ok := packer.add_file(file, file)
		if add_ok != OK:
			push_error("add_file failed: %s %s" % [file, add_ok])
			quit(1)
			
	var add_external_ok := add_external_dir(packer, external_asset_dir, "res://RemoveMultiplayerPlayerLimit")
	if add_external_ok != OK:
		push_error("add_external_dir failed: %s" % add_external_ok)
		quit(1)
	var image_target_path := "res://RemoveMultiplayerPlayerLimit/mod_image.png"
	if FileAccess.file_exists(external_asset_dir.path_join("mod_image.png")):
		var add_import_chain_ok := add_mod_image_import_chain(packer, project_dir, image_target_path)
		if add_import_chain_ok != OK:
			push_error("add_mod_image_import_chain failed: %s" % add_import_chain_ok)
			quit(1)
	var flush_ok := packer.flush()
	if flush_ok != OK:
		push_error("flush failed: %s" % flush_ok)
		quit(1)
	print("PCK built: %s" % output_file)
	quit(0)